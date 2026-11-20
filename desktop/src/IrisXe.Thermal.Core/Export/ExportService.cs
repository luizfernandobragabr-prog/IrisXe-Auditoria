namespace IrisXe.Thermal.Core.Export;

using System.Text;
using System.Text.Json;
using IrisXe.Thermal.Core.Diagnostics;

/// <summary>
/// Exportadores de resultados (requisito 10.5): CSV, JSON e NDJSON do log de auditoria e
/// das transicoes. Nenhum metodo altera hardware; a escrita em disco e feita pela interface
/// via SaveFileDialog (nada aqui toca no sistema de arquivos — mantem o nucleo puro e testavel).
/// </summary>
public static class ExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string AuditCsv(IReadOnlyList<ReadingRecord> records)
    {
        var sb = new StringBuilder();
        sb.AppendLine("timestamp_utc;processor_group;processor_number;msr_address;msr_name;mechanism;status;raw_hex;win32_error;error;simulated");
        foreach (var r in records)
        {
            sb.Append(r.TimestampUtc.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append(';')
              .Append(r.ProcessorGroup).Append(';')
              .Append(r.ProcessorNumber).Append(';')
              .Append($"0x{r.MsrAddress:X}").Append(';')
              .Append(Escape(r.MsrName)).Append(';')
              .Append(Escape(r.Mechanism)).Append(';')
              .Append(r.Status).Append(';')
              .Append(r.RawHex ?? string.Empty).Append(';')
              .Append(r.Win32Error?.ToString() ?? string.Empty).Append(';')
              .Append(Escape(r.Error ?? string.Empty)).Append(';')
              .Append(r.Simulated ? "sim" : "nao")
              .AppendLine();
        }

        return sb.ToString();
    }

    public static string Ndjson(IReadOnlyList<ReadingRecord> records)
    {
        var sb = new StringBuilder();
        foreach (var record in records)
        {
            sb.AppendLine(JsonSerializer.Serialize(record, JsonOptions));
        }

        return sb.ToString();
    }

    /// <summary>Exportacao JSON consolidada: auditoria + transicoes + rotulagem de modo.</summary>
    public static string DiagnosticsJson(
        IReadOnlyList<ReadingRecord> records,
        IReadOnlyList<TransitionEvent> transitions,
        string mechanismName,
        bool simulated,
        IReadOnlyList<string> notes)
    {
        var payload = new
        {
            projeto = "IRIS XE THERMAL MSR READER",
            gerado_em_utc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff"),
            mecanismo = mechanismName,
            modo_simulado = simulated,
            aviso = simulated
                ? "Os valores deste arquivo sao de teste manual (SIMULADOS) e nao representam leituras do hardware."
                : null,
            operacoes_de_escrita_realizadas = 0,
            notas = notes,
            transicoes = transitions.Select(t => new
            {
                t.DetectedAtUtc,
                t.MsrAddress,
                t.Scope,
                t.Indicator,
                t.Kind,
                t.From,
                t.To,
                t.IntervalSeconds,
                t.HonestTimestampNote,
            }),
            auditoria = records,
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static string Escape(string field) =>
        field.Contains(';') || field.Contains('"') || field.Contains('\n')
            ? $"\"{field.Replace("\"", "\"\"")}\""
            : field;
}
