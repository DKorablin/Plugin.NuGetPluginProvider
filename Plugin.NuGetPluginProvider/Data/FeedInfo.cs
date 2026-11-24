using System;
using Newtonsoft.Json;

namespace Plugin.NuGetPluginProvider.Data
{
	/// <summary>NuGet feed (package source) description</summary>
	public class FeedInfo
	{
		/// <summary>Feed name used in logs</summary>
		public String Name { get; set; }

		/// <summary>NuGet V3 service index URL (…/index.json)</summary>
		public String Url { get; set; }

		/// <summary>Optional user name. Environment variables in the %NAME% form are expanded</summary>
		public String UserName { get; set; }

		/// <summary>Optional password or token. Environment variables in the %NAME% form are expanded</summary>
		public String Password { get; set; }

		/// <summary>Disabled feeds are ignored</summary>
		public Boolean Enabled { get; set; } = true;

		/// <summary>The password is stored in the manifest as plain text instead of an environment variable reference</summary>
		[JsonIgnore]
		public Boolean IsPlainTextSecret
			=> !String.IsNullOrEmpty(this.Password) && !IsVariableReference(this.Password);

		/// <summary>Get the user name with expanded environment variables</summary>
		/// <returns>User name or null</returns>
		public String GetUserName()
			=> Expand(this.UserName);

		/// <summary>Get the password with expanded environment variables</summary>
		/// <returns>Password or null when it is not set or the environment variable is missing</returns>
		public String GetPassword()
			=> Expand(this.Password);

		/// <summary>Expand %NAME% environment variable references</summary>
		/// <param name="value">Value to expand</param>
		/// <returns>Expanded value or null when the value is empty or references an undefined variable</returns>
		internal static String Expand(String value)
		{
			if(String.IsNullOrEmpty(value))
				return null;

			String result = Environment.ExpandEnvironmentVariables(value);
			// ExpandEnvironmentVariables leaves undefined references untouched
			return IsVariableReference(result)
				? null
				: result;
		}

		/// <summary>The value is an environment variable reference in the %NAME% form</summary>
		/// <param name="value">Value to check</param>
		/// <returns>The value starts and ends with %</returns>
		private static Boolean IsVariableReference(String value)
			=> value.Length > 1 && value[0] == '%' && value[value.Length - 1] == '%';

		/// <inheritdoc/>
		public override String ToString()
			=> this.Name ?? this.Url;
	}
}