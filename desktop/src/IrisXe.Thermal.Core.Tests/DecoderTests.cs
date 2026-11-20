using IrisXe.Thermal.Core.Decoding;
using Xunit;

namespace IrisXe.Thermal.Core.Tests;

/// <summary>
/// Vetores de teste da decodificacao. Os valores sao EXEMPLOS DETERMINISTICOS construidos
/// a partir das definicoes documentadas (nao sao leituras de hardware) — servem para validar
/// mascaras, deslocamentos e calculos contra as fontes citadas em cada decodificador.
/// </summary>
public class TemperatureTargetDecodeTests
{
    private const string DsTgl = "631121-012";

    [Fact]
    public void TjMax_100_Offset13_Tau0_Decodifica_CasoDoEnunciado()
    {
        // 0x0D640000: TjMAX=0x64 (100 °C) em [23:16]; offset 13 em [29:24]; Tau=0 em [6:0].
        var decoded = TemperatureTargetDecoding.Decode(0x0D64_0000);

        Assert.Equal(100, decoded.TjMaxC);
        Assert.Equal(13, decoded.OffsetTgl);
        Assert.Equal(13, decoded.OffsetSdm);
        Assert.False(decoded.OffsetDivergent);
        Assert.Equal(0, decoded.TauRaw);
        Assert.Equal(87, decoded.AverageTargetC);
    }

    [Fact]
    public void TjMax_NaoPresumido_105C()
    {
        // 0x00690055: TjMAX=0x69 (105 °C); offset 0; Tau=0x55 (85) — bruto, sem conversao.
        var decoded = TemperatureTargetDecoding.Decode(0x0069_0055);

        Assert.Equal(105, decoded.TjMaxC);
        Assert.Equal(0, decoded.OffsetTgl);
        Assert.Equal(105, decoded.AverageTargetC);
        Assert.Equal(0x55, decoded.TauRaw);
    }

    [Fact]
    public void DivergenciaEntreMascaras_E_Sinalizada()
    {
        // 0x10640000: bits [29:28]=01 setados — mascara TGL [29:24] le 16; mascara SDM [27:24] le 0.
        var decoded = TemperatureTargetDecoding.Decode(0x1064_0000);

        Assert.Equal(16, decoded.OffsetTgl);
        Assert.Equal(0, decoded.OffsetSdm);
        Assert.True(decoded.OffsetDivergent);
        Assert.Equal(84, decoded.AverageTargetC);
    }

    [Fact]
    public void CamposReservados_SaoListadosSemInterpretacao()
    {
        var decoded = TemperatureTargetDecoding.Decode(0xFFFF_FFFF_FFFF_FFFF);

        var reserved = decoded.Fields.Where(f => f.Kind == "Reservado").ToList();
        Assert.Contains(reserved, f => f.Bits == "[63:30]");
        Assert.Contains(reserved, f => f.Bits == "[15:7]");
        Assert.All(reserved, f => Assert.Contains("NAO interpretad", f.Meaning, StringComparison.OrdinalIgnoreCase));
        // E a citacao do datasheet Tiger Lake aparece nos campos decodificados:
        Assert.Contains(decoded.Fields, f => f.Citation.Contains(DsTgl));
    }

    [Fact]
    public void Tau_NaoTemConversaoParaSegundos_LimitacaoDeclarada()
    {
        var decoded = TemperatureTargetDecoding.Decode(0x0D64_0000 | 0x7F);

        Assert.Equal(0x7F, decoded.TauRaw);
        var tauField = decoded.Fields.Single(f => f.Name.Contains("Tau"));
        Assert.Contains("LIMITACAO", tauField.Note, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NAO e documentada", tauField.Note, StringComparison.OrdinalIgnoreCase);
    }
}

public class ThermStatusDecodeTests
{
    [Fact]
    public void LeituraValida_CalculaTemperatura_TjMaxMenosReadout()
    {
        // 0x883C0000: bit31=1 (valido); resolucao [30:27]=1; readout [22:16]=0x3C (60).
        var decoded = ThermStatusDecoding.Decode(0x883C_0000, tjMaxC: 100);

        Assert.True(decoded.ReadingValid);
        Assert.Equal(60, decoded.DigitalReadout);
        Assert.Equal(1, decoded.ResolutionC);
        Assert.Equal(40, decoded.TemperatureC);
        Assert.False(decoded.ThermalStatus);
        Assert.False(decoded.ProchotEvent);
    }

    [Fact]
    public void ThrottlingAtivoComLog()
    {
        // 0x88120003: bit0=1 (status termico corrente), bit1=1 (log), readout=0x12 (18).
        var decoded = ThermStatusDecoding.Decode(0x8812_0003, tjMaxC: 100);

        Assert.True(decoded.ThermalStatus);
        Assert.True(decoded.ThermalStatusLog);
        Assert.Equal(82, decoded.TemperatureC);
    }

    [Fact]
    public void ReadoutInvalido_TemperaturaNaoDisponivel()
    {
        // bit31=0: leitura invalida — temperatura deve ser nula (nunca presumida).
        var decoded = ThermStatusDecoding.Decode(0x0000_003C, tjMaxC: 100);

        Assert.False(decoded.ReadingValid);
        Assert.Null(decoded.TemperatureC);
    }

    [Fact]
    public void ProchotExternoCorrente_ClassificadoComoExterno()
    {
        // 0x8820000C: bit2=1 (PROCHOT event corrente por componente externo), bit3=1 (log),
        // bit0=0 (sem evento termico interno), readout=0x20 (32).
        var decoded = ThermStatusDecoding.Decode(0x8820_000C, tjMaxC: 100);

        Assert.True(decoded.ProchotEvent);
        Assert.True(decoded.ProchotLog);
        Assert.False(decoded.ThermalStatus);
        Assert.Equal(68, decoded.TemperatureC);

        var pkg = PackageThermStatusDecoding.Decode(0x0000_0000, tjMaxC: 100);
        var assessment = ProchotAssessment.Assess(decoded, pkg);
        Assert.Contains("externo", assessment.Classification, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NAO identifica qual dispositivo fisico", assessment.Caveat);
    }
}

public class PackageThermStatusDecodeTests
{
    [Fact]
    public void Pacote_DecodificaReadoutELogs()
    {
        // 0x88280808: readout 0x28 (40) -> 60 °C; bit3=1 (0x8, PROCHOT log); bit11=1 (0x800, power limitation log).
        var decoded = PackageThermStatusDecoding.Decode(0x8828_0808, tjMaxC: 100);

        Assert.Equal(40, decoded.DigitalReadout);
        Assert.Equal(60, decoded.TemperatureC);
        Assert.True(decoded.ProchotLog);
        Assert.True(decoded.PowerLimitLog);
        Assert.False(decoded.ThermalStatus);
        Assert.False(decoded.ProchotEvent);
    }

    [Fact]
    public void BitsAcimaDe22_SaoTratadosComoNaoDocumentados()
    {
        var decoded = PackageThermStatusDecoding.Decode(0xFFFF_FFFF_FFFF_FFFF, tjMaxC: 100);

        var undocumented = decoded.Fields.Single(f => f.Name.StartsWith("Nao documentado"));
        Assert.Equal("Reservado", undocumented.Kind);
        Assert.Contains("NAO interpretados", undocumented.Meaning);
    }

    [Fact]
    public void ApenasLogSetado_OrigemNaoConfirmavel()
    {
        var core = ThermStatusDecoding.Decode(0x0000_000A, tjMaxC: 100); // bits 1 e 3
        var pkg = PackageThermStatusDecoding.Decode(0x0000_0008, tjMaxC: 100); // bit 3

        var assessment = ProchotAssessment.Assess(core, pkg);

        Assert.Contains("nao confirmavel", assessment.Classification, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NAO pode ser confirmada", assessment.Caveat);
    }
}
