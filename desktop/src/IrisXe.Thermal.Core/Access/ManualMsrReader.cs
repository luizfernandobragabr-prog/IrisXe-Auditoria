namespace IrisXe.Thermal.Core.Access;

/// <summary>
/// Mecanismo de teste da FASE 1: entrega valores fornecidos MANUALMENTE pelo usuario
/// (por exemplo, capturados de outras ferramentas) para exercitar decodificacao,
/// motor de diagnostico e exportacao SEM hardware.
///
/// REGRA ABSOLUTA: todo resultado carrega Simulated = true e o mecanismo se identifica
/// como SIMULADO em todas as telas e no log de auditoria. Nunca e apresentado como
/// leitura real (requisito 12: "Nao apresentar valores simulados como se fossem leituras reais").
/// Enderecos fora da lista autorizada continuam recusados.
/// </summary>
public sealed class ManualMsrReader : IMsrReader
{
    private readonly object _lock = new();
    private readonly Dictionary<int, Dictionary<int, ulong>> _values = new(); // msr -> lp -> valor

    public const string MechanismLabel = "Valores manuais (SIMULADO — não é leitura de hardware)";

    public string MechanismName => MechanismLabel;

    public bool IsSimulated => true;

    public bool IsAvailable => false; // não há acesso REAL ao hardware neste modo

    public string StatusDescription =>
        "Modo de teste: os valores foram fornecidos manualmente pelo usuário e são marcados como SIMULADOS "
        + "em toda a interface e no log de auditoria. Nenhum acesso ao processador é realizado.";

    public void SetValue(int msrAddress, int processorNumber, ulong value)
    {
        if (!Security.ReadPolicy.IsAllowed(msrAddress))
        {
            throw new ArgumentOutOfRangeException(
                nameof(msrAddress),
                $"Endereço 0x{msrAddress:X} não é autorizado pelo projeto (permitidos: 0x1A2, 0x19C, 0x1B1).");
        }

        lock (_lock)
        {
            if (!_values.TryGetValue(msrAddress, out var perLp))
            {
                perLp = new Dictionary<int, ulong>();
                _values[msrAddress] = perLp;
            }

            perLp[processorNumber] = value;
        }
    }

    /// <summary>Aplica o mesmo valor a todos os processadores logicos informados.</summary>
    public void SetValueForAll(int msrAddress, IEnumerable<int> processorNumbers, ulong value)
    {
        foreach (var lp in processorNumbers)
        {
            SetValue(msrAddress, lp, value);
        }
    }

    public void ClearAll()
    {
        lock (_lock)
        {
            _values.Clear();
        }
    }

    public MsrReadResult Read(int processorGroup, int processorNumber, int msrAddress)
    {
        if (!Security.ReadPolicy.IsAllowed(msrAddress))
        {
            return MsrReadResult.Fail(
                $"Endereço 0x{msrAddress:X} não está na lista autorizada do projeto (0x1A2, 0x19C, 0x1B1). Leitura recusada.",
                87);
        }

        ulong? value;
        lock (_lock)
        {
            value = _values.TryGetValue(msrAddress, out var perLp)
                && perLp.TryGetValue(processorNumber, out var v) ? v : null;
        }

        return value is null
            ? MsrReadResult.Fail(
                $"Não disponível — nenhum valor manual foi fornecido para o MSR 0x{msrAddress:X} no processador lógico {processorNumber}.",
                234) // ERROR_MORE_DATA-like; motivo: ausência de valor de teste
            : MsrReadResult.Ok(value.Value, simulated: true);
    }
}
