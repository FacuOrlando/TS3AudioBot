// TS3AudioBot - An advanced Musicbot for Teamspeak 3
// Copyright (C) 2017  TS3AudioBot contributors
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the Open Software License v. 3.0
//
// You should have received a copy of the Open Software License along with this
// program. If not, see <https://opensource.org/licenses/OSL-3.0>.

namespace TS3AudioBot.Plugins
{
	using Config;
	using Helper;
	using System;
	using System.Collections.Generic;
	using System.Globalization;
	using System.IO;
	using System.Linq;
	using System.Text;

	// Start Plugin:
	// ! Start plugins before rights system to ensure all rights are loaded
	// - Get all commands
	// - Validate
	//   - 0/1 Plugin
	//     - Command name conflict
	//   - 0/1 Factory
	//     - Facory name conflict
	// - [ Instantiate plugin (Depending on type) ]
	// - Add commands to command manager
	// - Start config to system?

	public class PluginManager : IDisposable
	{
		private static readonly NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();

		public Dependency.CoreInjector CoreInjector { get; set; }

		private readonly ConfPlugins config;
		private readonly Dictionary<string, Plugin> plugins;
		private readonly HashSet<int> usedIds;

		public PluginManager(ConfPlugins config)
		{
			Util.Init(out plugins);
			Util.Init(out usedIds);
			this.config = config;
		}

		private void CheckAndClearPlugins(Bot bot)
		{
			ClearMissingFiles();
			CheckLocalPlugins(bot);
		}

		/// <summary>Updates the plugin dictionary with new and changed plugins.</summary>
		/// <param name="bot">A bot instance when the plugin is a bot local plugin.</param>
		private void CheckLocalPlugins(Bot bot)
		{
			var dir = new DirectoryInfo(config.Path);
			if (!dir.Exists)
				return;

			foreach (var file in dir.EnumerateFiles())
			{
				if (plugins.TryGetValue(file.Name, out var plugin))
				{
					switch (plugin.CheckStatus(bot))
					{
					case PluginStatus.Disabled:
					case PluginStatus.Active:
					case PluginStatus.NotAvailable:
						continue;
					case PluginStatus.Ready:
					case PluginStatus.Off:
					case PluginStatus.Error:
						plugin.Load();
						break;
					default:
						throw new ArgumentOutOfRangeException();
					}
				}
				else
				{
					if (IsIgnored(file))
						continue;

					plugin = new Plugin(file, GetFreeId(), config.WriteStatusFiles);

					if (plugin.Load() == PluginResponse.Disabled)
					{
						RemovePlugin(plugin);
						continue;
					}

					CoreInjector.TryInject(plugin);
					plugins.Add(file.Name, plugin);
				}
			}
		}

		/// <summary>Unloads all Plugins which have no corresponding file anymore and removes from the index list.</summary>
		private void ClearMissingFiles()
		{
			// at first find all missing and ignored files
			var missingFiles = plugins.Where(kvp =>
			{
				kvp.Value.File.Refresh();
				return !kvp.Value.File.Exists || IsIgnored(kvp.Value.File);
			}).ToArray();

			// unload if it is loaded and remove
			foreach (var misFile in missingFiles)
				RemovePlugin(misFile.Value);
		}

		public static bool IsIgnored(FileInfo file) =>
			(file.Extension != ".cs" && file.Extension != ".dll" && file.Extension != ".exe")
			|| File.Exists(file.FullName + ".ignore");

		private Plugin TryGetPlugin(string identifier)
		{
			if (plugins.TryGetValue(identifier, out var plugin))
				return plugin;

			if (int.TryParse(identifier, out int num))
				return plugins.Values.FirstOrDefault(p => p.Id == num);

			return plugins.Values.FirstOrDefault(p => p.Name == identifier);
		}

		private int GetFreeId()
		{
			int id = 0;
			while (usedIds.Contains(id))
				id++;
			usedIds.Add(id);
			return id;
		}

		public PluginResponse StartPlugin(string identifier, Bot bot)
		{
			CheckLocalPlugins(bot);

			return TryGetPlugin(identifier)?.Start(bot) ?? PluginResponse.PluginNotFound;
		}

		public PluginResponse StopPlugin(string identifier, Bot bot)
			=> TryGetPlugin(identifier)?.Stop(bot) ?? PluginResponse.PluginNotFound;

		internal void StopPlugins(Bot bot)
		{
			foreach (var plugin in plugins.Values)
			{
				if (plugin.Type == PluginType.BotPlugin && plugin.CheckStatus(bot) == PluginStatus.Active)
					plugin.Stop(bot);
			}
		}

		private void RemovePlugin(Plugin plugin)
		{
			usedIds.Remove(plugin.Id);
			plugins.Remove(plugin.File.Name);
			plugin.Unload();
		}

		public PluginStatusInfo[] GetPluginOverview(Bot bot)
		{
			CheckAndClearPlugins(bot);

			return plugins.Values.Select(plugin =>
				new PluginStatusInfo(
					plugin.Id,
					plugin.Name,
					plugin.CheckStatus(bot),
					plugin.Type
				)
			).ToArray();
		}

		public static string FormatOverview(ICollection<PluginStatusInfo> pluginList)
		{
			if (pluginList.Count == 0)
				return "No plugins found!";

			var strb = new StringBuilder();
			strb.AppendLine("All available plugins:");
			var digits = (int)Math.Floor(Math.Log10(pluginList.Count) + 1);
			foreach (var plugin in pluginList)
			{
				strb.Append("#").Append(plugin.Id.ToString("D" + digits, CultureInfo.InvariantCulture)).Append('|');
				switch (plugin.Status)
				{
				case PluginStatus.Off: strb.Append("OFF"); break;
				case PluginStatus.Ready: strb.Append("RDY"); break;
				case PluginStatus.Active: strb.Append("+ON"); break;
				case PluginStatus.Disabled: strb.Append("UNL"); break;
				case PluginStatus.Error: strb.Append("ERR"); break;
				case PluginStatus.NotAvailable: strb.Append("N/A"); break;
				default: throw Util.UnhandledDefault(plugin.Status);
				}
				strb.Append('|').AppendLine(plugin.Name ?? "<not loaded>");
			}
			return strb.ToString();
		}

		public void Dispose()
		{
			foreach (var plugin in plugins.Values)
				plugin.Unload();
		}
	}

	public class PluginStatusInfo
	{
		public int Id { get; }
		public string Name { get; }
		public PluginStatus Status { get; }
		public PluginType Type { get; }

		public PluginStatusInfo(int id, string name, PluginStatus status, PluginType type)
		{
			Id = id;
			Name = name;
			Status = status;
			Type = type;
		}
	}
}
