using IrisXe.Thermal.Core;
using IrisXe.Thermal.Core.Access;
using IrisXe.Thermal.Core.Diagnostics;
using IrisXe.Thermal.Core.Export;
using IrisXe.Thermal.Core.Hardware;
using IrisXe.Thermal.Core.Security;
using Xunit;

namespace IrisXe.Thermal.Core.Tests;

public class CpuSignatureTests
{
    [Fact]
    public void Parse_I7_1195G7_E_Reconhecido()
    {
        var signature = CpuSignature.Parse(
            "Intel64 Family 6 Model 140 Stepping 1",
            "11th Gen Intel(R) Core(TM) i7-1195G7 @ 2.90GHz");

        Assert.True(signature.IsIntelFamily6);
        Assert.True(signature.IsTigerLake1195G7);
        Assert.Null(signature.UnsupportedReason);
        Assert.Equal(0x8C, signature.Model);
    }

    [Fact]
    public void Parse_OutroModelo_E_Recusado()
    {
        var signature = CpuSignature.Parse("Intel64 Family 6 Model 126 Stepping 5", "Ice Lake CPU");

        Assert.False(signature.IsTigerLake1195G7);
        Assert.NotNull(signature.UnsupportedReason);
        Assert.Contains("recusadas", signature.UnsupportedReason);
    }

    [Fact]
    public void Parse_Amd_E_Recusado()
    {
        var signature = CpuSignature.Parse("AMD64 Family 25 Model 80 Stepping 0", "AMD Ryzen");

        Assert.False(signature.IsIntelFamily6);
        Assert.NotNull(signature.UnsupportedReason);
    }
}

public class ReadPolicyTests
{
    [Fact]
    public void Apenas_Os_Tres_Msrs_Sao_Autorizados()
    {
        Assert.True(ReadPolicy.IsAllowed(0x1A2));
        Assert.True(ReadPolicy.IsAllowed(0x19C));
        Assert.True(ReadPolicy.IsAllowed(0x1B1));
        Assert.False(ReadPolicy.IsAllowed(0xCE));   // MSR_PLATFORM_INFO
        Assert.False(ReadPolicy.IsAllowed(0x1FC));  // MSR_POWER_CTL
        Assert.False(ReadPolicy.IsAllowed(0xE8));   // MSR_RDTSC
    }

    [Fact]
    public void ManualReader_Recusa_EnderecosNaoAutorizados()
    {
        var reader = new ManualMsrReader();

        var result = reader.Read(0, 0, 0xCE);

        Assert.False(result.Success);
        Assert.Contains("não está na lista autorizada", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ManualReader_SetValue_EnderecoNaoAutorizado_LancaExcecao()
    {
        var reader = new ManualMsrReader();

        Assert.Throws<ArgumentOutOfRangeException>(() => reader.SetValue(0x610, 0, 0));
    }

    [Fact]
    public void ManualReader_SemValor_NaoDisponivelComMotivo()
    {
        var reader = new ManualMsrReader();

        var result = reader.Read(0, 3, MsrAddress.ThermStatus);

        Assert.False(result.Success);
        Assert.StartsWith("Não disponível", result.ErrorMessage);
    }

    [Fact]
    public void UnavailableReader_NuncaRetornaValor()
    {
        var reader = new UnavailableMsrReader();

        foreach (var address in MsrAddress.All)
        {
            var result = reader.Read(0, 0, address);
            Assert.False(result.Success);
            Assert.StartsWith("Não disponível", result.ErrorMessage);
            Assert.False(result.Simulated);
        }
    }

    [Fact]
    public void SelfCheck_DeclaraGarantiasEstruturais()
    {
        var checks = ReadPolicy.SelfCheck();

        Assert.Contains(checks, c => c.Assertion.Contains("escrita"));
        Assert.Contains(checks, c => c.Assertion.Contains("0x1A2"));
        Assert.All(checks, c => Assert.False(string.IsNullOrWhiteSpace(c.Evidence)));
    }
}
