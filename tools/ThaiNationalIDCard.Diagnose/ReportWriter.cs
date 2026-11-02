using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ThaiNationalIDCard.Hardware;
using ThaiNationalIDCard.Protocol;
using ThaiNationalIDCard.Workflow;

namespace ThaiNationalIDCard.Diagnose
{
    /// <summary>
    /// Renders diagnostics as text or JSON. Its only input is <see cref="ReadDiagnostics"/>, which has no way to hold
    /// card content, and that is the real guarantee: no code path here can reach a card byte.
    /// </summary>
    internal static class ReportWriter
    {
        public static string ToolVersion => VersionOf(typeof(ReportWriter).Assembly);

        public static string LibraryVersion => VersionOf(typeof(CardReadWorkflow).Assembly);

        public static void WriteText(TextWriter writer, CardReadMode mode, ReadDiagnostics? diagnostics)
        {
            writer.WriteLine("tool version: " + ToolVersion);
            writer.WriteLine("library version: " + LibraryVersion);
            writer.WriteLine("os: " + OsDescription());
            writer.WriteLine("mode: " + ModeName(mode));
            writer.WriteLine("reader: " + (diagnostics?.ReaderName ?? "none"));
            writer.WriteLine("protocol: " + (diagnostics?.Protocol?.ToString() ?? "none"));
            writer.WriteLine("atr: " + (string.IsNullOrEmpty(diagnostics?.AtrHex) ? "none" : diagnostics!.AtrHex));
            writer.WriteLine("p2 used: " + (diagnostics?.P2Used == null ? "none (no GET RESPONSE was sent)" : Hex2(diagnostics.P2Used.Value)));

            if (diagnostics == null)
            {
                return;
            }

            writer.WriteLine("readers:");
            foreach (ReaderDiagnostics reader in diagnostics.Readers)
            {
                writer.WriteLine("  " + reader.DisplayName + ": " + reader.State);
            }

            writer.WriteLine("trace:");
            int number = 1;
            foreach (TraceEntry entry in diagnostics.Trace)
            {
                writer.WriteLine("  " + number.ToString(CultureInfo.InvariantCulture) + " " + TraceLine(entry));
                number++;
            }

            writer.WriteLine("fields:");
            foreach (FieldDiagnostics field in diagnostics.Fields)
            {
                writer.WriteLine("  " + FieldLine(field));
            }
        }

        public static void WriteJson(Stream stream, CardReadMode mode, ReadDiagnostics? diagnostics, FailureReport? failure)
        {
            using var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
            json.WriteStartObject();
            json.WriteString("tool_version", ToolVersion);
            json.WriteString("library_version", LibraryVersion);
            json.WriteString("os", OsDescription());
            json.WriteString("mode", ModeName(mode));
            WriteOrNull(json, "reader", diagnostics?.ReaderName);
            WriteOrNull(json, "protocol", diagnostics?.Protocol?.ToString());
            WriteOrNull(json, "atr", string.IsNullOrEmpty(diagnostics?.AtrHex) ? null : diagnostics!.AtrHex);
            WriteOrNull(json, "p2_used", diagnostics?.P2Used == null ? null : Hex2(diagnostics.P2Used.Value));

            json.WriteStartArray("readers");
            foreach (ReaderDiagnostics reader in diagnostics?.Readers ?? Array.Empty<ReaderDiagnostics>())
            {
                json.WriteStartObject();
                json.WriteString("name", reader.DisplayName);
                json.WriteString("state", reader.State.ToString());
                json.WriteEndObject();
            }

            json.WriteEndArray();

            json.WriteStartArray("trace");
            foreach (TraceEntry entry in diagnostics?.Trace ?? Array.Empty<TraceEntry>())
            {
                WriteTraceEntry(json, entry);
            }

            json.WriteEndArray();

            json.WriteStartArray("fields");
            foreach (FieldDiagnostics field in diagnostics?.Fields ?? Array.Empty<FieldDiagnostics>())
            {
                WriteField(json, field);
            }

            json.WriteEndArray();

            if (failure == null)
            {
                json.WriteNull("error");
            }
            else
            {
                json.WriteStartObject("error");
                json.WriteString("code", failure.Code);
                json.WriteNumber("exit_code", failure.ExitCode);
                json.WriteString("message", failure.Message);
                WriteOrNull(json, "hint", failure.Hint);
                json.WriteEndObject();
            }

            json.WriteEndObject();
        }

        private static string VersionOf(Assembly assembly) =>
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

        private static string ModeName(CardReadMode mode) => mode == CardReadMode.Public ? "public" : "private";

        private static string OsDescription()
        {
            var text = new StringBuilder();
            foreach (char c in RuntimeInformation.OSDescription)
            {
                text.Append(c >= ' ' && c <= '~' ? c : ' ');
            }

            return text.ToString();
        }

        private static string Hex2(int value) => value.ToString("X2", CultureInfo.InvariantCulture);

        private static string Hex4(int value) => value.ToString("X4", CultureInfo.InvariantCulture);

        private static string Flags(Enum flags) => flags.ToString().Replace(", ", "|", StringComparison.Ordinal);

        private static string TraceLine(TraceEntry entry)
        {
            var parts = new List<string> { "kind=" + entry.Kind };
            if (entry.Kind == TraceCommandKind.Event)
            {
                parts.Add("event=" + entry.Event);
            }

            if (entry.Offset != TraceEntry.NoValue)
            {
                parts.Add("offset=" + Hex4(entry.Offset));
            }

            if (entry.RequestedLength != TraceEntry.NoValue)
            {
                parts.Add("requested=" + entry.RequestedLength.ToString(CultureInfo.InvariantCulture));
            }

            if (entry.ReceivedLength != TraceEntry.NoValue)
            {
                parts.Add("received=" + entry.ReceivedLength.ToString(CultureInfo.InvariantCulture));
            }

            if (entry.StatusWord != TraceEntry.NoValue)
            {
                parts.Add("sw=" + Hex4(entry.StatusWord));
            }

            if (entry.Kind != TraceCommandKind.Event)
            {
                parts.Add("round=" + entry.Round.ToString(CultureInfo.InvariantCulture));
            }

            if (entry.GetResponseP2 != TraceEntry.NoValue)
            {
                parts.Add("p2=" + Hex2(entry.GetResponseP2));
            }

            if (entry.Flags != ExchangeNotes.None)
            {
                parts.Add("flags=" + Flags(entry.Flags));
            }

            if (entry.Verdict != CommandVerdict.Allowed)
            {
                parts.Add("verdict=" + entry.Verdict);
            }

            if (entry.Failure != CardFailureKind.None)
            {
                parts.Add("failure=" + entry.Failure);
            }

            return string.Join(" ", parts);
        }

        private static void WriteTraceEntry(Utf8JsonWriter json, TraceEntry entry)
        {
            json.WriteStartObject();
            json.WriteString("kind", entry.Kind.ToString());
            WriteOrNull(json, "event", entry.Kind == TraceCommandKind.Event ? entry.Event.ToString() : null);
            WriteOrNull(json, "offset", entry.Offset == TraceEntry.NoValue ? null : Hex4(entry.Offset));
            WriteNumberOrNull(json, "requested", entry.RequestedLength);
            WriteNumberOrNull(json, "received", entry.ReceivedLength);
            WriteOrNull(json, "sw", entry.StatusWord == TraceEntry.NoValue ? null : Hex4(entry.StatusWord));
            WriteNumberOrNull(json, "round", entry.Round);
            WriteOrNull(json, "p2", entry.GetResponseP2 == TraceEntry.NoValue ? null : Hex2(entry.GetResponseP2));
            json.WriteString("flags", Flags(entry.Flags));
            json.WriteString("verdict", entry.Verdict.ToString());
            json.WriteString("failure", entry.Failure.ToString());
            json.WriteEndObject();
        }

        private static string Result(FieldQuality quality)
        {
            switch (quality)
            {
                case FieldQuality.Ok:
                    return "pass";
                case FieldQuality.NotRead:
                    return "not-read";
                default:
                    return "fail";
            }
        }

        private static string FieldLine(FieldDiagnostics field)
        {
            var parts = new List<string> { field.Id + ": " + Result(field.Quality), "quality=" + field.Quality };

            if (field.StatusWord != TraceEntry.NoValue)
            {
                parts.Add("sw=" + Hex4(field.StatusWord));
            }

            if (field.RequestedLength != TraceEntry.NoValue)
            {
                parts.Add("requested=" + field.RequestedLength.ToString(CultureInfo.InvariantCulture));
            }

            parts.Add("received=" + field.ReceivedLength.ToString(CultureInfo.InvariantCulture));
            parts.Add("issues=" + Flags(field.Issues));
            parts.Add("checks_failed=" + Flags(field.Validation));
            parts.Add("flags=" + Flags(field.Flags));

            if (field.Failure != CardFailureKind.None)
            {
                parts.Add("failure=" + field.Failure);
            }

            return string.Join(" ", parts);
        }

        private static void WriteField(Utf8JsonWriter json, FieldDiagnostics field)
        {
            json.WriteStartObject();
            json.WriteString("field", field.Id.ToString());
            json.WriteString("result", Result(field.Quality));
            json.WriteString("quality", field.Quality.ToString());
            WriteOrNull(json, "sw", field.StatusWord == TraceEntry.NoValue ? null : Hex4(field.StatusWord));
            WriteNumberOrNull(json, "requested", field.RequestedLength);
            json.WriteNumber("received", field.ReceivedLength);
            json.WriteString("issues", Flags(field.Issues));
            json.WriteString("checks_failed", Flags(field.Validation));
            json.WriteString("flags", Flags(field.Flags));
            json.WriteString("failure", field.Failure.ToString());
            json.WriteEndObject();
        }

        private static void WriteOrNull(Utf8JsonWriter json, string name, string? value)
        {
            if (value == null)
            {
                json.WriteNull(name);
            }
            else
            {
                json.WriteString(name, value);
            }
        }

        private static void WriteNumberOrNull(Utf8JsonWriter json, string name, int value)
        {
            if (value == TraceEntry.NoValue)
            {
                json.WriteNull(name);
            }
            else
            {
                json.WriteNumber(name, value);
            }
        }
    }
}
