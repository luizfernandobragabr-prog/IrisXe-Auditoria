namespace IrisXe.Thermal.Core.Diagnostics;

using IrisXe.Thermal.Core.Decoding;

/// <summary>
/// Comparador de amostras consecutivas: registra transicoes dos indicadores (correntes e
/// historicos) dos nucleos e do pacote, sem nunca limpar nada e sem escrever nos registradores.
/// </summary>
public static class TransitionDetector
{
    public static IReadOnlyList<TransitionEvent> Diff(SampleSnapshot? previous, SampleSnapshot current)
    {
        var events = new List<TransitionEvent>();
        var interval = previous is null ? 0 : (current.TimestampUtc - previous.TimestampUtc).TotalSeconds;

        foreach (var reading in current.CoreReadings)
        {
            if (reading.Decoded is null)
            {
                continue;
            }

            var prev = previous?.CoreReadings.FirstOrDefault(r => r.ProcessorNumber == reading.ProcessorNumber)?.Decoded;
            Compare(events, prev, reading.Decoded, $"Núcleo (LP {reading.ProcessorNumber})", MsrAddress.ThermStatus, interval);
        }

        if (current.Package is not null)
        {
            var prev = previous?.Package;
            ComparePackage(events, prev, current.Package, interval);
        }

        return events;
    }

    private static void Compare(
        List<TransitionEvent> events,
        ThermStatusDecoding? previous,
        ThermStatusDecoding current,
        string scope,
        int msr,
        double interval)
    {
        Add(events, msr, scope, "Thermal Status", "Corrente",
            previous?.ThermalStatus, current.ThermalStatus, interval, TransitionEvent.State, TransitionEvent.State);
        Add(events, msr, scope, "Thermal Status Log", "Histórico",
            previous?.ThermalStatusLog, current.ThermalStatusLog, interval, TransitionEvent.LogState, TransitionEvent.LogState);
        Add(events, msr, scope, "PROCHOT#/FORCEPR# Event", "Corrente",
            previous?.ProchotEvent, current.ProchotEvent, interval, TransitionEvent.State, TransitionEvent.State);
        Add(events, msr, scope, "PROCHOT#/FORCEPR# Log", "Histórico",
            previous?.ProchotLog, current.ProchotLog, interval, TransitionEvent.LogState, TransitionEvent.LogState);
        Add(events, msr, scope, "Critical Temperature Status", "Corrente",
            previous?.CriticalTempStatus, current.CriticalTempStatus, interval, TransitionEvent.State, TransitionEvent.State);
        Add(events, msr, scope, "Critical Temperature Status Log", "Histórico",
            previous?.CriticalTempLog, current.CriticalTempLog, interval, TransitionEvent.LogState, TransitionEvent.LogState);
        Add(events, msr, scope, "Power Limitation Status", "Corrente",
            previous?.PowerLimitStatus, current.PowerLimitStatus, interval, TransitionEvent.State, TransitionEvent.State);
        Add(events, msr, scope, "Power Limitation Log", "Histórico",
            previous?.PowerLimitLog, current.PowerLimitLog, interval, TransitionEvent.LogState, TransitionEvent.LogState);
    }

    private static void ComparePackage(
        List<TransitionEvent> events,
        PackageThermStatusDecoding? previous,
        PackageThermStatusDecoding current,
        double interval)
    {
        Add(events, 0x1B1, "Pacote", "Package Thermal Status", "Corrente",
            previous?.ThermalStatus, current.ThermalStatus, interval, TransitionEvent.State, TransitionEvent.State);
        Add(events, 0x1B1, "Pacote", "Package Thermal Status Log", "Histórico",
            previous?.ThermalStatusLog, current.ThermalStatusLog, interval, TransitionEvent.LogState, TransitionEvent.LogState);
        Add(events, 0x1B1, "Pacote", "Package PROCHOT# Event", "Corrente",
            previous?.ProchotEvent, current.ProchotEvent, interval, TransitionEvent.State, TransitionEvent.State);
        Add(events, 0x1B1, "Pacote", "Package PROCHOT# Log", "Histórico",
            previous?.ProchotLog, current.ProchotLog, interval, TransitionEvent.LogState, TransitionEvent.LogState);
        Add(events, 0x1B1, "Pacote", "Package Critical Temperature Status", "Corrente",
            previous?.CriticalTempStatus, current.CriticalTempStatus, interval, TransitionEvent.State, TransitionEvent.State);
        Add(events, 0x1B1, "Pacote", "Package Critical Temperature Status Log", "Histórico",
            previous?.CriticalTempLog, current.CriticalTempLog, interval, TransitionEvent.LogState, TransitionEvent.LogState);
        Add(events, 0x1B1, "Pacote", "Package Power Limitation Status", "Corrente",
            previous?.PowerLimitStatus, current.PowerLimitStatus, interval, TransitionEvent.State, TransitionEvent.State);
        Add(events, 0x1B1, "Pacote", "Package Power Limitation Log", "Histórico",
            previous?.PowerLimitLog, current.PowerLimitLog, interval, TransitionEvent.LogState, TransitionEvent.LogState);
    }

    private static void Add(
        List<TransitionEvent> events,
        int msr,
        string scope,
        string indicator,
        string kind,
        bool? before,
        bool after,
        double interval,
        Func<bool, string> formatBefore,
        Func<bool, string> formatAfter)
    {
        if (before is null || before == after)
        {
            return;
        }

        events.Add(new TransitionEvent
        {
            DetectedAtUtc = DateTime.UtcNow,
            MsrAddress = msr,
            Scope = scope,
            Indicator = indicator,
            Kind = kind,
            From = formatBefore(before.Value),
            To = formatAfter(after),
            IntervalSeconds = interval <= 0 ? 0 : interval,
        });
    }
}
