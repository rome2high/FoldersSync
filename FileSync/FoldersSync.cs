using System;
using System.Collections.Generic;
using System.Deployment.Application;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Synchronization;
using Microsoft.Synchronization.Files;

namespace FoldersSync
{
	// Copyright (c) Microsoft Corporation.  All rights reserved.
	// Modified to meet personal needs
	// Author: Romeo Mai
	// Email: RomeoMai@Outlook.com
	// Notes: Create root directory to hold metadata and log files. Modify filesync.config file to you need then add to
	// root directory. Program will automatically run if .config file exist else user enter directories using the console.

	public class FoldersSync
	{
		public static string[] FilterExtension;
		private const int Sleeptime = 15; //15 Minutes
		private const int LogMaxLength = 5000000; //5mB
		private const string RootDir = @"C:\filesync";
		private static bool exitSystem;

		#region Trap application termination

		[DllImport("Kernel32")]
		private static extern bool SetConsoleCtrlHandler(EventHandler handler, bool add);

		private delegate bool EventHandler(CtrlType sig);

		private static EventHandler _handler;

		private enum CtrlType
		{
			CTRL_C_EVENT = 0,
			CTRL_BREAK_EVENT = 1,
			CTRL_CLOSE_EVENT = 2,
			CTRL_LOGOFF_EVENT = 5,
			CTRL_SHUTDOWN_EVENT = 6
		}

		private static bool Handler(CtrlType sig)
		{
			//do your cleanup here
			Thread.Sleep(1000); //simulate some cleanup delay
			Console.WriteLine("Cleanup complete");

			Log(new ApplicationException("Application End")); //log end time

			//allow main to run off
			exitSystem = true;

			//shutdown right away so there are no lingering threads
			Environment.Exit(-1);

			return true;
		}

		#endregion

		[STAThread]
		public static void Main(string[] args)
		{
			if (!PathExists(RootDir))
				Directory.CreateDirectory(RootDir);

			InstallUpdates();
			_handler += new EventHandler(Handler);
			SetConsoleCtrlHandler(_handler, true);

			#region Display Console

			Console.Title = "Audit Synchronyzation Program";
			Console.SetWindowPosition(0, 0);
			Console.SetWindowSize(100, Console.LargestWindowHeight);

			var v = GetRunningVersion();
			var about = string.Format(CultureInfo.InvariantCulture, @" v{0}.{1}.{2}(r{3})", v.Major, v.Minor, v.Build, v.Revision);
			var t1 = "---------------------------------------";
			var t2 = "Real Time Synchronizing Program";

			Console.SetCursorPosition(Console.WindowWidth/2 - t1.Length/2, Console.CursorTop);
			Console.WriteLine(t1);
			Console.SetCursorPosition(Console.WindowWidth/2 - t2.Length/2, Console.CursorTop);
			Console.WriteLine(t2);
			Console.SetCursorPosition(Console.WindowWidth/2 - about.Length/2, Console.CursorTop);
			Console.WriteLine("{0}", about);
			Console.SetCursorPosition(Console.WindowWidth/2 - t1.Length/2, Console.CursorTop);
			Console.WriteLine(t1);

			#endregion

			Log(new ApplicationException("Application started")); //log start time

			//FileSync.config entry point
			try
			{
				FileInfo fullpath = new FileInfo(RootDir + "\\FileSync.config");

				if (fullpath.Exists)
				{
					args = new string[2];

					var lines = File.ReadAllLines(fullpath.FullName);
					for (int i = 0; i < 2; i++)
					{
						string[] s = lines[i].Split(',');
						args.SetValue(s.GetValue(1).ToString().Trim(), i);
					}

					try
					{
						FilterExtension = lines[2].Split(',').GetValue(1).ToString().Split(';');
					}
					catch
					{
						Console.WriteLine("You don't have any exclusion file extention" +
						                  "\n you can add them below line 'target' in Finlesync.config" +
						                  "\n e.g. exlude_Extentions, *.mdb;*.jpg;*gif;*.bmp;");
					}
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine(
					"Error: Failed to read for FileSync.config" +
					"\nUsage: add to FileSync.config file source and destination valid directory path!");
				Log(ex);
				return;
			}

			//cmd entry point
			if (args.Length == 2)
			{
				if (string.IsNullOrEmpty(args[0]) || string.IsNullOrEmpty(args[1]) ||
				    !Directory.Exists(args[0]) || !Directory.Exists(args[1]))
				{
					Console.WriteLine(
						"Invalid entry, please check if directories exist!\n" +
						"Usage: FileSync [valid_directory_path_1] [valid_directory_path_2]");
					Log(new DirectoryNotFoundException(@"Source = " + args[0] + ", Target=" + args[1]));
					return;
				}
			}
			//.exe entry point
			else
			{
				bool isValid = false;
				args = new string[2];

				while (!isValid)
				{
					try
					{
						Console.Write("Enter valid directory path 1: ");
						var line = Console.ReadLine();
						var source = new DirectoryInfo(line ?? "");
						if (source.Name.ToLower().Contains("exit"))
							return;
						Console.Write("Enter valid directory path 2: ");
						line = Console.ReadLine();
						var dest = new DirectoryInfo(line ?? "");
						if (dest.Name.ToLower().Contains("exit"))
							return;
						if (source.Exists && dest.Exists)
						{
							args.SetValue(source.FullName, 0);
							args.SetValue(dest.FullName, 1);
							isValid = true;
						}
						else
							Console.WriteLine("Error: One or more directories not exist, " +
							                  "\nTry again or enter (exit) to close program");
					}
					catch (Exception ex)
					{
						Console.WriteLine("INVALID INPUT, try again!");
						Log(ex);
					}
				}
			}
			Console.WriteLine(
				"--- BEGIN MONITORING ---" +
				"\n Local source Folder: " + args[0] +
				"\n Remote target Folder: " + args[1]);

			AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

			while (true)
			{
				try
				{
					sync_Main(args);
				}
				catch (Exception ex)
				{
					Log(ex);
				}
				//sleep for 15min before retry
				Thread.Sleep(Sleeptime*60000);
			}
		}

		private static void InstallUpdates()
		{
			if (ApplicationDeployment.IsNetworkDeployed)
			{
				var appDev = ApplicationDeployment.CurrentDeployment;
				try
				{
					var info = appDev.CheckForDetailedUpdate();
					if (info.UpdateAvailable)
					{
						appDev.Update();
						Log(new ApplicationException("Version Updated: " + appDev.UpdatedVersion));
						//restart
						var stringPath =
							@"%USERPROFILE%\AppData\Roaming\Microsoft\Internet Explorer\Quick Launch\User Pinned\StartMenu\Audits-sp_Sync.appref-ms";
						var filePath = Environment.ExpandEnvironmentVariables(stringPath);
						Process.Start(filePath);
						Thread.Sleep(1000);
						Environment.Exit(0);
					}
				}
				catch (Exception ex)
				{
					Log(ex);
				}
			}
		}

		public static void sync_Main(string[] args)
		{
			string replica1RootPath = args[0];
			string replica2RootPath = args[1];
			string s_metadata = "source_metadata";
			string t_metadata = "target_metadata";
			DirectoryInfo syncDir = new DirectoryInfo(RootDir);

			if (!syncDir.Exists)
				syncDir.Create();

			SyncId sourceId = GetSyncId(syncDir.FullName + "\\Source_File.ID");
			SyncId destId = GetSyncId(syncDir.FullName + "\\Target_File.ID");

			// Set options for the synchronization operation
			FileSyncOptions options = FileSyncOptions.ExplicitDetectChanges |
			                          FileSyncOptions.RecycleDeletedFiles |
			                          FileSyncOptions.RecyclePreviousFileOnUpdates |
			                          FileSyncOptions.RecycleConflictLoserFiles;

			//exclude file name or extentions
			FileSyncScopeFilter filter = new FileSyncScopeFilter();
			//filter.FileNameExcludes.Add("*.lnk"); // Exclude all *.lnk files

			if (FilterExtension != null)
			{
				foreach (string s in FilterExtension)
				{
					if (!String.IsNullOrEmpty(s))
					{
						filter.FileNameExcludes.Add(s);
					}
				}
			}
			//keep me alive
			while (true)
			{
				// Explicitly detect changes on both replicas upfront, to avoid two change
				// detection passes for the two-way synchronization
				DetectChangesOnFileSystemReplica(
					sourceId, replica1RootPath, filter, options, syncDir, s_metadata);
				DetectChangesOnFileSystemReplica(
					destId, replica2RootPath, filter, options, syncDir, t_metadata);

				// Synchronization in both directions
				SyncFileSystemReplicasOneWay(
					sourceId, destId, replica1RootPath, replica2RootPath, syncDir, s_metadata, t_metadata, null, options);
				Thread.Sleep(1000);
				SyncFileSystemReplicasOneWay(
					destId, sourceId, replica2RootPath, replica1RootPath, syncDir, t_metadata, s_metadata, null, options);
				Thread.Sleep(5000);
			}
		}

		private static void Log(Exception ex)
		{
			Console.WriteLine(DateTime.Now + " - " + ex.Message);

			using (
				FileStream sourceStream = File.Open(RootDir + @"\fileSyncLog.log", FileMode.OpenOrCreate))
			{
				byte[] header = new UTF8Encoding(true).GetBytes(Environment.NewLine + DateTime.Now + " ");
				byte[] info = new UTF8Encoding(true).GetBytes(ex.ToString());
				IEnumerable<byte> outBytes = header.Concat(info);

				if (sourceStream.Length < LogMaxLength)
				{
					sourceStream.Seek(0, SeekOrigin.End);
					var enumerable = outBytes as byte[] ?? outBytes.ToArray();
					sourceStream.Write(enumerable, 0, enumerable.Length);
				}
				else
				{
					sourceStream.Close();
					var fileInfo = new FileInfo(sourceStream.Name);
					fileInfo.Delete();
					Log(ex);
				}
			}
		}

		public static class MinidumpType
		{
			public const int MiniDumpNormal = 0x00000000;
			public const int MiniDumpWithDataSegs = 0x00000001;
			public const int MiniDumpWithFullMemory = 0x00000002;
			public const int MiniDumpWithHandleData = 0x00000004;
			public const int MiniDumpFilterMemory = 0x00000008;
			public const int MiniDumpScanMemory = 0x00000010;
			public const int MiniDumpWithUnloadedModules = 0x00000020;
			public const int MiniDumpWithIndirectlyReferencedMemory = 0x00000040;
			public const int MiniDumpFilterModulePaths = 0x00000080;
			public const int MiniDumpWithProcessThreadData = 0x00000100;
			public const int MiniDumpWithPrivateReadWriteMemory = 0x00000200;
			public const int MiniDumpWithoutOptionalData = 0x00000400;
			public const int MiniDumpWithFullMemoryInfo = 0x00000800;
			public const int MiniDumpWithThreadInfo = 0x00001000;
			public const int MiniDumpWithCodeSegs = 0x00002000;
		}

		[DllImport("dbghelp.dll")]
		public static extern bool MiniDumpWriteDump(IntPtr hProcess,
			Int32 processId,
			IntPtr hFile,
			int dumpType,
			IntPtr exceptionParam,
			IntPtr userStreamParam,
			IntPtr callackParam);

		private static void CurrentDomain_UnhandledException(object sender,
			UnhandledExceptionEventArgs unhandledExceptionEventArgs)
		{
			CreateMiniDump();
		}

		private static void CreateMiniDump()
		{
			using (FileStream fs = new FileStream("UnhandledDump.dmp", FileMode.Create))
			{
				using (Process process = Process.GetCurrentProcess())
				{
					if (fs.SafeFileHandle != null)
						MiniDumpWriteDump(process.Handle,
							process.Id,
							fs.SafeFileHandle.DangerousGetHandle(),
							MinidumpType.MiniDumpNormal,
							IntPtr.Zero,
							IntPtr.Zero,
							IntPtr.Zero);
				}
			}
		}

		public static void DetectChangesOnFileSystemReplica(
			SyncId syncid, string replicaRootPath,
			FileSyncScopeFilter filter, FileSyncOptions options,
			DirectoryInfo syncDir,
			string metadataName)
		{
			FileSyncProvider provider = null;

			try
			{
				provider = new FileSyncProvider(
					syncid.GetGuidId(), replicaRootPath, filter, options, syncDir.FullName, metadataName, syncDir.FullName, null);
				provider.DetectChanges();
			}
			finally
			{
				// Release resources
				if (provider != null)
					provider.Dispose();
			}
		}

		public static void SyncFileSystemReplicasOneWay(
			SyncId sourceId, SyncId destId,
			string sourceReplicaRootPath, string destinationReplicaRootPath,
			DirectoryInfo syncDir,
			string sourceMetadata, string targetMetadata,
			FileSyncScopeFilter filter, FileSyncOptions options)
		{
			FileSyncProvider sourceProvider = null;
			FileSyncProvider destinationProvider = null;

			try
			{
				sourceProvider = new FileSyncProvider(
					sourceId.GetGuidId(), sourceReplicaRootPath, filter, options, syncDir.FullName, sourceMetadata, syncDir.FullName,
					null);
				destinationProvider = new FileSyncProvider(
					destId.GetGuidId(), destinationReplicaRootPath, filter, options, syncDir.FullName, targetMetadata, syncDir.FullName,
					null);

				destinationProvider.AppliedChange += OnAppliedChange;
				destinationProvider.SkippedChange += OnSkippedChange;

				SyncOrchestrator agent = new SyncOrchestrator
				{
					LocalProvider = sourceProvider,
					RemoteProvider = destinationProvider,
					Direction = SyncDirectionOrder.Upload
				};
				// Sync source to destination

				//Console.WriteLine("Synchronizing changes to replica: " +
				//   destinationProvider.RootDirectoryPath);
				agent.Synchronize();
			}
			finally
			{
				// Release resources
				if (sourceProvider != null) sourceProvider.Dispose();
				if (destinationProvider != null) destinationProvider.Dispose();
			}
		}

		public static void OnAppliedChange(object sender, AppliedChangeEventArgs args)
		{
			switch (args.ChangeType)
			{
				case ChangeType.Create:
					Console.WriteLine("-- Applied CREATE for file " + args.NewFilePath);
					break;
				case ChangeType.Delete:
					Console.WriteLine("-- Applied DELETE for file " + args.OldFilePath);
					break;
				case ChangeType.Update:
					Console.WriteLine("-- Applied OVERWRITE for file " + args.OldFilePath);
					break;
				case ChangeType.Rename:
					Console.WriteLine("-- Applied RENAME for file " + args.OldFilePath +
					                  " as " + args.NewFilePath);
					break;
			}
		}

		public static void OnSkippedChange(object sender, SkippedChangeEventArgs args)
		{
			if (args.Exception != null)
			{
				//fastest string comparison
				string[] ss = args.Exception.Message.Split(' ');
				string[] sf = "0x80070005".Split(' ');
				//application can't delete file on server. location of this file unknow. rm-09/2015
				int[] c = new int[sf.Length];

				foreach (string str in ss)
				{
					for (int y = 0; y < sf.Length; y++)
					{
						c[y] += ((str.Length - str.Replace(sf[y], String.Empty).Length)/sf[y].Length > 0 ? 1 : 0);
					}
				}

				if (c[0] > 0)
				{
					return;
				}

				Console.WriteLine("-- Skipped applying " + args.ChangeType.ToString().ToUpper()
				                  + " for " + (!string.IsNullOrEmpty(args.CurrentFilePath)
					                  ? args.CurrentFilePath
					                  : args.NewFilePath) + " due to error");
				Console.WriteLine("   [" + args.Exception.Message + "]");
			}
		}

		private static SyncId GetSyncId(string syncFilePath)
		{
			Guid guid;
			SyncId replicaId;
			if (!File.Exists(syncFilePath)) //The ID file doesn't exist. 
				//Create the file and store the guid which is used to 
				//instantiate the instance of the SyncId.
			{
				guid = Guid.NewGuid();
				replicaId = new SyncId(guid);
				FileStream fs = File.Open(syncFilePath, FileMode.Create);
				StreamWriter sw = new StreamWriter(fs);
				sw.WriteLine(guid.ToString());
				sw.Close();
				fs.Close();
			}
			else
			{
				FileStream fs = File.Open(syncFilePath, FileMode.Open);
				StreamReader sr = new StreamReader(fs);
				string guidString = sr.ReadLine();
				guid = new Guid(guidString);
				replicaId = new SyncId(guid);
				sr.Close();
				fs.Close();
			}
			return (replicaId);
		}

		private static Version GetRunningVersion()
		{
			try
			{
				return ApplicationDeployment.CurrentDeployment.CurrentVersion;
			}
			catch
			{
				return Assembly.GetExecutingAssembly().GetName().Version;
			}
		}

		internal static bool PathExists(string name)
		{
			return (Directory.Exists(name) || File.Exists(name));
		}
	}
}