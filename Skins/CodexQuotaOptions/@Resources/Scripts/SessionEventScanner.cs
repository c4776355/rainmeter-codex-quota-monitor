using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CodexQuota
{
    public sealed class SessionEvent
    {
        public string Type { get; set; }
        public string TurnId { get; set; }
        public long TimestampUnix { get; set; }
    }

    public sealed class SessionScanResult
    {
        public long Offset { get; set; }
        public List<SessionEvent> Events { get; private set; }

        public SessionScanResult()
        {
            Events = new List<SessionEvent>();
        }
    }

    public static class SessionEventScanner
    {
        private const int PrefixLimit = 768;
        private const int BufferSize = 65536;

        public static SessionScanResult Scan(string path, long requestedOffset)
        {
            SessionScanResult result = new SessionScanResult();
            byte[] buffer = new byte[BufferSize];
            byte[] prefix = new byte[PrefixLimit];
            int prefixLength = 0;

            using (FileStream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                BufferSize,
                FileOptions.SequentialScan))
            {
                long offset = requestedOffset;
                if (offset < 0 || offset > stream.Length)
                {
                    offset = 0;
                }

                stream.Seek(offset, SeekOrigin.Begin);
                long absolutePosition = offset;
                long lastCompleteLine = offset;
                int count;

                while ((count = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    for (int index = 0; index < count; index++)
                    {
                        byte current = buffer[index];
                        absolutePosition++;

                        if (current == 10)
                        {
                            ParsePrefix(prefix, prefixLength, result.Events);
                            prefixLength = 0;
                            lastCompleteLine = absolutePosition;
                        }
                        else if (prefixLength < prefix.Length)
                        {
                            prefix[prefixLength++] = current;
                        }
                    }
                }

                // Keep the offset at the start of an incomplete final line so a
                // later append can be parsed without retaining the line in RAM.
                result.Offset = lastCompleteLine;
            }

            return result;
        }

        private static void ParsePrefix(byte[] prefix, int length, List<SessionEvent> events)
        {
            if (length <= 0)
            {
                return;
            }

            string text = Encoding.UTF8.GetString(prefix, 0, length);
            const string payloadMarker = "\"payload\":{\"type\":\"";
            int payloadIndex = text.IndexOf(payloadMarker, StringComparison.Ordinal);
            if (payloadIndex < 0)
            {
                return;
            }

            int typeStart = payloadIndex + payloadMarker.Length;
            int typeEnd = text.IndexOf('"', typeStart);
            if (typeEnd < 0)
            {
                return;
            }

            string eventType = text.Substring(typeStart, typeEnd - typeStart);
            if (eventType != "task_started" && eventType != "task_complete" && eventType != "turn_aborted")
            {
                return;
            }

            const string turnMarker = "\"turn_id\":\"";
            int turnIndex = text.IndexOf(turnMarker, typeEnd, StringComparison.Ordinal);
            if (turnIndex < 0)
            {
                return;
            }

            int turnStart = turnIndex + turnMarker.Length;
            int turnEnd = text.IndexOf('"', turnStart);
            if (turnEnd < 0)
            {
                return;
            }

            long eventTimestamp = ReadInteger(text, eventType == "task_started" ? "\"started_at\":" : "\"completed_at\":", turnEnd);
            events.Add(new SessionEvent
            {
                Type = eventType,
                TurnId = text.Substring(turnStart, turnEnd - turnStart),
                TimestampUnix = eventTimestamp
            });
        }

        private static long ReadInteger(string text, string marker, int startIndex)
        {
            int markerIndex = text.IndexOf(marker, startIndex, StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                return 0;
            }

            int valueStart = markerIndex + marker.Length;
            int valueEnd = valueStart;
            while (valueEnd < text.Length && text[valueEnd] >= '0' && text[valueEnd] <= '9')
            {
                valueEnd++;
            }

            long value;
            if (valueEnd == valueStart || !Int64.TryParse(text.Substring(valueStart, valueEnd - valueStart), out value))
            {
                return 0;
            }
            return value;
        }
    }
}
