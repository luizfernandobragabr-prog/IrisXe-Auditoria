namespace IrisXe.Thermal.Core.Diagnostics;

using IrisXe.Thermal.Core.Access;
using IrisXe.Thermal.Core.Decoding;

/// <summary>
/// Motor de diagnostico (requisito 9.4): coordena amostragens, compara leituras, registra
/// transicoes e produz diagnosticos — SEM modificar o hardware. Somente-leitura por heranca
/// do contrato <see cref="IMsrReader"/>, que nao possui operacoes de escrita.
/// </summary>
public sealed class DiagnosticEngine
{
    private IMsrReader _reader;
    private readonly AuditLog _audit;
    private readonly List<(int Group, int Number)> _logicalProcessors;
    private readonly object _lock = new();

    public DiagnosticEngine(IMsrReader reader, AuditLog audit, IEnumerable<(int Group, int Number)> logicalProcessors)
    {
        _reader = reader;
        _audit = audit;
        _logicalProcessors = logicalProcessors.OrderBy(p => p.Group).ThenBy(p => p.Number).ToList();
    }

    public IMsrReader Reader => _reader;

    public AuditLog Audit => _audit;

    public SampleSnapshot? LastSample { get; private set; }

    /// <summary>Transicoes acumuladas desde o inicio da sessao — nunca limpas.</summary>
    public List<TransitionEvent> TransitionLog { get; } = new();

    /// <summary>Levantamento de topologia: um processador logico por grupo.</summary>
    public IReadOnlyList<(int Group, int Number)> LogicalProcessors => _logicalProcessors;

    /// <summary>
    /// Substitui o mecanismo de leitura (ex.: trocar de "Indisponivel" para "valores manuais").
    /// Somente leitura: qualquer leitor respeita o contrato IMsrReader, que nao possui escrita.
    /// </summary>
    public void SetReader(IMsrReader reader) =>
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));

    /// <summary>
    /// Executa UMA rodada de amostragem (somente RDMSR conceitual; na Fase 1 os valores vem
    /// do mecanismo configurado — indisponivel ou manual/simulado). Registra auditoria,
    /// decodifica e detecta transicoes em relacao a amostra anterior.
    /// </summary>
    public SampleSnapshot CaptureSample()
    {
        lock (_lock)
        {
            var notes = new List<string>();
            var timestamp = DateTime.UtcNow;

            // --- 0x19C por processador logico (escopo documentado: por LP) ----------------
            var coreReadings = new List<LogicalProcessorReading>();
            int? tjMax = LastSample?.TemperatureTarget?.TjMaxC;
            foreach (var (group, number) in _logicalProcessors)
            {
                var result = _reader.Read(group, number, MsrAddress.ThermStatus);
                _audit.Add(new ReadingRecord
                {
                    TimestampUtc = result.TimestampUtc == default ? timestamp : result.TimestampUtc,
                    ProcessorGroup = group,
                    ProcessorNumber = number,
                    MsrAddress = MsrAddress.ThermStatus,
                    MsrName = MsrAddress.Name(MsrAddress.ThermStatus),
                    Mechanism = _reader.MechanismName,
                    Status = result.Success ? "Sucesso" : "Falha",
                    RawHex = result.Success ? result.ValueHex : null,
                    Error = result.ErrorMessage,
                    Win32Error = result.Win32Error,
                    Simulated = result.Simulated,
                });

                ThermStatusDecoding? decoded = null;
                if (result.Success)
                {
                    decoded = ThermStatusDecoding.Decode(result.Value, tjMax);
                }

                coreReadings.Add(new LogicalProcessorReading
                {
                    ProcessorGroup = group, ProcessorNumber = number, Result = result, Decoded = decoded,
                });
            }

            // --- 0x1B1 por LP para conferencia (escopo: pacote) ---------------------------
            var packageReadings = new List<LogicalProcessorReading>();
            MsrReadResult? firstPackageResult = null;
            foreach (var (group, number) in _logicalProcessors)
            {
                var result = _reader.Read(group, number, MsrAddress.PackageThermStatus);
                _audit.Add(new ReadingRecord
                {
                    TimestampUtc = result.TimestampUtc == default ? timestamp : result.TimestampUtc,
                    ProcessorGroup = group,
                    ProcessorNumber = number,
                    MsrAddress = MsrAddress.PackageThermStatus,
                    MsrName = MsrAddress.Name(MsrAddress.PackageThermStatus),
                    Mechanism = _reader.MechanismName,
                    Status = result.Success ? "Sucesso" : "Falha",
                    RawHex = result.Success ? result.ValueHex : null,
                    Error = result.ErrorMessage,
                    Win32Error = result.Win32Error,
                    Simulated = result.Simulated,
                });

                if (result.Success && firstPackageResult is null)
                {
                    firstPackageResult = result;
                }

                packageReadings.Add(new LogicalProcessorReading
                {
                    ProcessorGroup = group,
                    ProcessorNumber = number,
                    Result = result,
                    Decoded = null, // pacote nao tem decodificacao por-LP; exibida uma vez, abaixo
                });
            }

            var packageDecoded = firstPackageResult is not null
                ? PackageThermStatusDecoding.Decode(firstPackageResult.Value, tjMax)
                : null;

            // Conferencia: leituras do pacote divergentes entre LPs NAO sao descartadas.
            var distinctPackage = packageReadings
                .Where(r => r.Result.Success)
                .Select(r => r.Result.Value)
                .Distinct()
                .ToList();
            if (distinctPackage.Count > 1)
            {
                notes.Add(
                    "MSR_PACKAGE_THERM_STATUS (0x1B1) retornou valores diferentes entre processadores logicos "
                    + $"({string.Join(", ", distinctPackage.Select(Bits.Hex16))}). Escopo documentado e por pacote; "
                    + "a divergencia NAO foi descartada e deve ser investigada (pode indicar leitura em momentos "
                    + "diferentes do mesmo contador ou comportamento de virtualizacao).");
            }

            // --- 0x1A2 por LP para conferencia (valores de fabrica devem ser iguais) ------
            var targetReadings = new List<MsrReadResult>();
            TemperatureTargetDecoding? targetDecoded = null;
            foreach (var (group, number) in _logicalProcessors)
            {
                var result = _reader.Read(group, number, MsrAddress.TemperatureTarget);
                _audit.Add(new ReadingRecord
                {
                    TimestampUtc = result.TimestampUtc == default ? timestamp : result.TimestampUtc,
                    ProcessorGroup = group,
                    ProcessorNumber = number,
                    MsrAddress = MsrAddress.TemperatureTarget,
                    MsrName = MsrAddress.Name(MsrAddress.TemperatureTarget),
                    Mechanism = _reader.MechanismName,
                    Status = result.Success ? "Sucesso" : "Falha",
                    RawHex = result.Success ? result.ValueHex : null,
                    Error = result.ErrorMessage,
                    Win32Error = result.Win32Error,
                    Simulated = result.Simulated,
                });

                targetReadings.Add(result);
                if (result.Success && targetDecoded is null)
                {
                    targetDecoded = TemperatureTargetDecoding.Decode(result.Value);
                }
            }

            var distinctTargets = targetReadings
                .Where(r => r.Success)
                .Select(r => r.Value)
                .Distinct()
                .ToList();
            if (distinctTargets.Count > 1)
            {
                notes.Add(
                    "MSR_TEMPERATURE_TARGET (0x1A2) retornou valores diferentes entre processadores logicos "
                    + $"({string.Join(", ", distinctTargets.Select(Bits.Hex16))}). Esperado: valor calibrado em fabrica "
                    + "identico. A divergencia NAO foi descartada — cada valor permanece no log de auditoria.");
            }

            if (coreReadings.All(r => !r.Result.Success))
            {
                notes.Add(
                    "Nenhuma leitura de IA32_THERM_STATUS foi bem-sucedida nesta amostragem. Os campos exibirao "
                    + "'Nao disponivel' com o motivo registrado no log de auditoria (nenhum valor presumido e exibido).");
            }

            var snapshot = new SampleSnapshot
            {
                TimestampUtc = timestamp,
                CoreReadings = coreReadings,
                PackageReadings = packageReadings,
                TemperatureTargetReadings = targetReadings,
                TemperatureTarget = targetDecoded,
                Package = packageDecoded,
                Notes = notes,
                MechanismName = _reader.MechanismName,
                Simulated = _reader.IsSimulated,
            };

            // Transicoes em relacao a amostra anterior (requisito §6) — nunca limpas.
            foreach (var transition in TransitionDetector.Diff(LastSample, snapshot))
            {
                TransitionLog.Add(transition);
            }

            LastSample = snapshot;
            return snapshot;
        }
    }
}
