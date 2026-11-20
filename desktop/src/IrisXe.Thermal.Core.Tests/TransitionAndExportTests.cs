using IrisXe.Thermal.Core.Diagnostics;
using IrisXe.Thermal.Core.Export;
using Xunit;

namespace IrisXe.Thermal.Core.Tests;

public class TransitionAndExportTests
{
    private static (int Group, int Number)[] Lps() => Enumerable.Range(0, 8).Select(i => (0, i)).ToArray();

    private static SampleSnapshot SampleWith(ulong thermStatus, ulong? package = null, ulong target = 0x0D64_0000)
    {
        var manual = new Access.ManualMsrReader();
        foreach (var (_, lp) in Lps())
        {
            manual.SetValue(MsrAddress.ThermStatus, lp, thermStatus);
            manual.SetValue(MsrAddress.TemperatureTarget, lp, target);
            if (package is not null)
            {
                manual.SetValue(MsrAddress.PackageThermStatus, lp, package.Value);
            }
        }

        var audit = new AuditLog();
        var engine = new DiagnosticEngine(manual, audit, Lps());
        return engine.CaptureSample();
    }

    [Fact]
    public void Transicao_Ativacao_E_Detectada_ComNotaHonestade()
    {
        var before = SampleWith(0x883C_0000); // sem evento
        var after = SampleWith(0x883C_0003);  // bit0 e bit1 ativados

        var transitions = TransitionDetector.Diff(before, after);

        Assert.Contains(transitions, t => t.Indicator == "Thermal Status" && t.From == "inativo" && t.To == "ativo");
        Assert.Contains(transitions, t => t.Indicator == "Thermal Status Log" && t.To == "registrado");
        Assert.All(transitions, t => Assert.Contains("não pode ser determinado", t.HonestTimestampNote));
    }

    [Fact]
    public void Transicoes_SemMudanca_Nao_GeramEventos()
    {
        var before = SampleWith(0x883C_0000);
        var after = SampleWith(0x883C_0000);

        Assert.Empty(TransitionDetector.Diff(before, after));
    }

    [Fact]
    public void Auditoria_Registra_CamposDoRequisito_E_ExportaCsv()
    {
        var audit = new AuditLog();
        var engine = new DiagnosticEngine(new Access.ManualMsrReader(), audit, Lps());
        engine.CaptureSample();

        var csv = ExportService.AuditCsv(audit.Snapshot());

        Assert.Contains("timestamp_utc", csv);
        Assert.Contains("processor_number", csv);
        Assert.Contains("msr_address", csv);
        Assert.Contains("raw_hex", csv);
        Assert.Contains("simulated", csv);
        Assert.Contains("0x19C", csv);
        Assert.Contains("0x1A2", csv);
        Assert.Contains("0x1B1", csv);
        // Modo manual é sempre rotulado como simulado:
        Assert.Contains(";sim", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportacaoJson_DeclaraZeroOperacoesDeEscrita()
    {
        var audit = new AuditLog();
        var engine = new DiagnosticEngine(new Access.ManualMsrReader(), audit, Lps());
        engine.CaptureSample();

        var json = ExportService.DiagnosticsJson(
            audit.Snapshot(), engine.TransitionLog, engine.Reader.MechanismName, engine.Reader.IsSimulated, Array.Empty<string>());

        Assert.Contains("\"operacoes_de_escrita_realizadas\": 0", json);
        Assert.Contains("SIMULADOS", json); // aviso de modo simulado presente
    }

    [Fact]
    public void Motor_ComLeitorIndisponivel_RegistraFalhaSemValoresPresumidos()
    {
        var audit = new AuditLog();
        var engine = new DiagnosticEngine(new Access.UnavailableMsrReader(), audit, Lps());

        var sample = engine.CaptureSample();

        Assert.Null(sample.TemperatureTarget);
        Assert.Null(sample.Package);
        Assert.All(sample.CoreReadings, r => Assert.False(r.Result.Success));
        Assert.Contains(audit.Snapshot(), r => r.Status == "Falha" && r.RawHex is null);
        Assert.Contains(sample.Notes, n => n.Contains("Nao disponivel") || n.Contains("Não disponível"));
    }

    [Fact]
    public void ComparacaoThrottleStop_DetectaMudancaE_TraziAviso()
    {
        var engine = new DiagnosticEngine(new Access.ManualMsrReader(), new AuditLog(), Lps());
        // Preenche manual: antes offset 13 (0x0D640000), depois offset 13 inalterado em 0x1A2,
        // mas com mudanca no status termico (simulando um evento entre as amostras).
        var manual = (Access.ManualMsrReader)engine.Reader;
        foreach (var (_, lp) in Lps())
        {
            manual.SetValue(MsrAddress.TemperatureTarget, lp, 0x0D64_0000);
            manual.SetValue(MsrAddress.PackageThermStatus, lp, 0x8828_0000);
            manual.SetValue(MsrAddress.ThermStatus, lp, 0x883C_0000);
        }

        var before = ThrottleStopComparison.FromSample(engine.CaptureSample(), "ANTES");
        foreach (var (_, lp) in Lps())
        {
            manual.SetValue(MsrAddress.ThermStatus, lp, 0x883C_0003); // mudanca
        }

        var after = ThrottleStopComparison.FromSample(engine.CaptureSample(), "DEPOIS");
        var report = ThrottleStopComparison.Compare(before, after);

        Assert.True(report.Simulated);
        Assert.Contains(report.Entries, e => e.MsrAddress == MsrAddress.ThermStatus && e.Changed);
        Assert.DoesNotContain(report.Entries, e => e.MsrAddress == MsrAddress.TemperatureTarget && e.Changed);
        Assert.Contains("NAO prova causalidade", ComparisonReport.Disclaimer);
    }
}
