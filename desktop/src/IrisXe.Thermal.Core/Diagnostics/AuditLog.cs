namespace IrisXe.Thermal.Core.Diagnostics;

/// <summary>
/// Log de auditoria APENAS PARA ACRESCIMO: os registros nunca sao removidos nem limpos
/// (requisito 14.1-D). Thread-safe. Nada aqui toca no hardware.
/// </summary>
public sealed class AuditLog
{
    private readonly object _lock = new();
    private readonly List<ReadingRecord> _records = new();

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _records.Count;
            }
        }
    }

    public void Add(ReadingRecord record)
    {
        lock (_lock)
        {
            _records.Add(record);
        }
    }

    /// <summary>Copia imutavel (ordenada por horario) para exibicao e exportacao.</summary>
    public IReadOnlyList<ReadingRecord> Snapshot()
    {
        lock (_lock)
        {
            return _records.OrderBy(r => r.TimestampUtc).ToList();
        }
    }
}
