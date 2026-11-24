using System;
using System.Diagnostics;
using System.Threading.Tasks;
using NuGet.Common;
using SAL.Flatbed;

namespace Plugin.NuGetPluginProvider.Domain
{
	/// <summary>Writes NuGet client messages to the plugin trace source</summary>
	internal class TraceLogger : LoggerBase
	{
		private readonly ITraceSource _trace;

		public TraceLogger(ITraceSource trace)
			: base(LogLevel.Verbose)
			=> this._trace = trace ?? throw new ArgumentNullException(nameof(trace));

		/// <remarks>
		/// NuGet messages are written as verbose: every HTTP request is logged as Information and failed requests are retried with warnings.
		/// The provider reports failed operations itself
		/// </remarks>
		public override void Log(ILogMessage message)
			=> this._trace.TraceEvent(TraceEventType.Verbose, 10, "NuGet {0}: {1}", message.Level, message.Message);

		public override Task LogAsync(ILogMessage message)
		{
			this.Log(message);
			return Task.CompletedTask;
		}
	}
}