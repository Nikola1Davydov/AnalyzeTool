using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace IdsSharp
{
    /// <summary>What buildingSMART's own auditor made of a file.</summary>
    public sealed class IdsAuditResult
    {
        internal IdsAuditResult(bool ok, bool hasErrors, string status, IReadOnlyList<string> messages)
        {
            Ok = ok;
            HasErrors = hasErrors;
            Status = status;
            Messages = messages;
        }

        /// <summary>Nothing at all was reported — not even a warning.</summary>
        public bool Ok { get; }

        /// <summary>Something was reported that is an ERROR rather than a warning. This is the one to
        /// branch on: a file with structure warnings is still checkable, a file with errors is not.</summary>
        public bool HasErrors { get; }

        /// <summary>The auditor's flags, spelled out — e.g. "XsdSchemaError, IdsContentError".</summary>
        public string Status { get; }

        /// <summary>Everything the auditor said, in the order it said it.</summary>
        public IReadOnlyList<string> Messages { get; }

        public string Describe() =>
            Messages.Count == 0 ? Status : Status + ": " + string.Join("; ", Messages);
    }

    /// <summary>
    /// Checks that a file really is valid IDS, using <c>ids-lib</c> — buildingSMART's own auditor.
    ///
    /// <para>Kept apart from <see cref="IdsParser"/> because the two answer different questions and
    /// want different temperaments. The parser is deliberately tolerant: files in the wild carry
    /// several versions of the IDS namespace and some carry none, and refusing them would be
    /// strictness that helps nobody. The audit is deliberately strict: it is how you find out whether
    /// the specification you were handed is actually well-formed before you trust a report built from
    /// it.</para>
    ///
    /// <para>Delegated rather than hand-rolled on purpose. Reimplementing the XSD and the format's
    /// implementation agreements would produce a checker that is subtly wrong in ways only the
    /// official suite would catch — and a checker that accepts an invalid specification and then
    /// reports a clean model is precisely the failure this library exists to prevent.</para>
    /// </summary>
    public static class IdsAudit
    {
        public static IdsAuditResult Check(string xml)
        {
            if (xml == null) throw new ArgumentNullException(nameof(xml));

            using MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
            return Check(stream);
        }

        public static IdsAuditResult CheckFile(string path)
        {
            if (!File.Exists(path)) throw new IdsParseException($"There is no IDS file at '{path}'.");

            using FileStream stream = File.OpenRead(path);
            return Check(stream);
        }

        public static IdsAuditResult Check(Stream idsSource)
        {
            if (idsSource == null) throw new ArgumentNullException(nameof(idsSource));

            CollectingLogger logger = new CollectingLogger();

            // IdsVersion carries its own default, so the audit runs against the version ids-lib
            // considers current rather than one pinned here and quietly going stale.
            IdsLib.Audit.Status status =
                IdsLib.Audit.Run(idsSource, new IdsLib.SingleAuditOptions(), logger);

            bool ok = status == IdsLib.Audit.Status.Ok;

            // Status is a [Flags] enum whose warning member is named as one. Everything else that is
            // set is an error, which keeps this correct if a later version adds members.
            bool hasErrors =
                !ok && (status & ~IdsLib.Audit.Status.IdsStructureWarning) != IdsLib.Audit.Status.Ok;

            return new IdsAuditResult(ok, hasErrors, status.ToString(), logger.Messages);
        }

        /// <summary>An ILogger that keeps what it is told. The audit's verdict is one flags value; the
        /// part a person can act on only comes out through the logger.</summary>
        private sealed class CollectingLogger : ILogger
        {
            public List<string> Messages { get; } = new List<string>();

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (formatter == null) return;

                string message = formatter(state, exception);
                if (!string.IsNullOrWhiteSpace(message)) Messages.Add(message);
            }
        }
    }
}
