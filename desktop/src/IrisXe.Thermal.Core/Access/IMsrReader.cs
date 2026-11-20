namespace IrisXe.Thermal.Core.Access;

/// <summary>Resultado de uma tentativa de leitura de MSR.</summary>
public sealed record MsrReadResult
{
    public required bool Success { get; init; }

    public required ulong Value { get; init; }

    /// <summary>Motivo da falha, em portugues, quando Success = false.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Codigo Win32 associado, quando aplicavel (ex.: 5 = Acesso Negado).</summary>
    public int? Win32Error { get; init; }

    /// <summary>Verdadeiro quando o valor NAO veio do hardware (modo de teste manual).</summary>
    public required bool Simulated { get; init; }

    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;

    public static MsrReadResult Ok(ulong value, bool simulated = false) => new()
    { Success = true, Value = value, Simulated = simulated };

    public static MsrReadResult Fail(string message, int? win32Error = null) => new()
    { Success = false, Value = 0, ErrorMessage = message, Win32Error = win32Error, Simulated = false };

    public string ValueHex => $"0x{Value:X16}";
}

/// <summary>
/// Contrato do mecanismo de acesso a MSR. POR CONSTRUCAO, a interface nao declara nenhuma
/// operacao de escrita — e a garantia estrutural central do projeto (requisito 8.1/8.4):
/// o compilador torna impossivel ao restante do codigo invocar um WRMSR, pois nao existe
/// membro para isso.
///
/// Implementacoes previstas:
///   - Fase 1: <see cref="UnavailableMsrReader"/> (sem driver instalado) e
///     <see cref="ManualMsrReader"/> (valores manuais de teste, sempre rotulados como simulados).
///   - Fase 2: leitor sobre o driver proprio somente-leitura, apos revisao e autorizacao.
/// </summary>
public interface IMsrReader
{
    /// <summary>Nome do mecanismo exibido na interface (estado do mecanismo de leitura).</summary>
    string MechanismName { get; }

    /// <summary>Verdadeiro quando os valores NAO vem do hardware (teste manual).</summary>
    bool IsSimulated { get; }

    /// <summary>Verdadeiro quando o mecanismo consegue realizar leituras reais.</summary>
    bool IsAvailable { get; }

    /// <summary>Descricao do estado/limitacao atual do mecanismo, para exibicao.</summary>
    string StatusDescription { get; }

    /// <summary>
    /// Le um MSR em um processador logico especifico (grupo, numero). Recusa enderecos fora
    /// da lista autorizada (<see cref="Security.ReadPolicy"/>) sem tocar no hardware.
    /// </summary>
    MsrReadResult Read(int processorGroup, int processorNumber, int msrAddress);
}
