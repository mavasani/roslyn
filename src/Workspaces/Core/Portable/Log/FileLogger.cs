// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics.Tracing;
using System.IO;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Options;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Internal.Log
{
    /// <summary>
    /// A logger that publishes events to a log file.
    /// </summary>
    internal sealed class FileLogger : ILogger
    {
        private readonly object _gate;
        private readonly string _file;
        private readonly StringBuilder _buffer;
        private bool _enabled;

        public FileLogger(IGlobalOptionService optionService, string logFilePath)
        {
            _file = logFilePath;
            _gate = new();
            _buffer = new();
            _enabled = optionService.GetOption(InternalDiagnosticsOptions.EnableFileLoggingForTelemetry);
            optionService.OptionChanged += OptionService_OptionChanged;
        }

        public FileLogger(IGlobalOptionService optionService)
            : this(optionService, Path.Combine(Path.GetTempPath(), "Roslyn", "Telemetry", GetLogFileName()))
        {
        }

        private void OptionService_OptionChanged(object? sender, OptionChangedEventArgs e)
        {
            if (e.Option == InternalDiagnosticsOptions.EnableFileLoggingForTelemetry)
            {
                Contract.ThrowIfNull(e.Value);

                _enabled = (bool)e.Value;
            }
        }

        private static string GetLogFileName()
            => DateTime.Now.ToString().Replace(' ', '_').Replace('/', '_').Replace(':', '_') + ".log";

        public bool IsEnabled(FunctionId functionId)
        {
            if (!_enabled)
            {
                return false;
            }

            // Limit logged function IDs to keep a reasonable log file size.
            var str = functionId.ToString();
            return str.StartsWith("Diagnostic") ||
                str.StartsWith("CodeAnalysisService") ||
                str.StartsWith("Workspace") ||
                str.StartsWith("WorkCoordinator") ||
                str.StartsWith("IncrementalAnalyzerProcessor") ||
                str.StartsWith("ExternalErrorDiagnosticUpdateSource");
        }

        private void Log(FunctionId functionId, string message)
        {
            lock (_gate)
            {
                _buffer.AppendLine($"{DateTime.Now} ({functionId}) : {message}");

                try
                {
                    if (!File.Exists(_file))
                    {
                        Directory.CreateDirectory(PathUtilities.GetDirectoryName(_file));
                        File.Create(_file);
                    }

                    File.AppendAllText(_file, _buffer.ToString());
                    _buffer.Clear();
                }
                catch (IOException)
                {
                }
            }
        }

        public void Log(FunctionId functionId, LogMessage logMessage)
            => Log(functionId, logMessage.GetMessage());

        public void LogBlockStart(FunctionId functionId, LogMessage logMessage, int uniquePairId, CancellationToken cancellationToken)
            => LogBlockEvent(functionId, logMessage, uniquePairId, "BlockStart");

        public void LogBlockEnd(FunctionId functionId, LogMessage logMessage, int uniquePairId, int delta, CancellationToken cancellationToken)
            => LogBlockEvent(functionId, logMessage, uniquePairId, cancellationToken.IsCancellationRequested ? "BlockCancelled" : "BlockEnd");

        private void LogBlockEvent(FunctionId functionId, LogMessage logMessage, int uniquePairId, string blockEvent)
            => Log(functionId, $"[{blockEvent} - {uniquePairId}] {logMessage.GetMessage()}");
    }
}
