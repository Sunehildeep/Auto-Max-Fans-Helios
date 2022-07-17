using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using TsDotNetLib;
using OpenHardwareMonitor.Hardware;
using System.Security.Principal;
using System.Diagnostics;
using System.Windows;

namespace AutoMaxFans
{
    class Program
    {
		private static int CPU_Fan_amount;

		private static int GPU_Fan_amount;

		private static int System_Fan_amount;

		private static bool overheating = false;

		private static bool changedonce = false;

		enum Fan_Mode_Type
        {
			Auto = 0,
			Max = 1,
			Custom = 2
        }


		private static void initilizeFanAmount()
		{
			CPU_Fan_amount = 1;
			GPU_Fan_amount = 1;
			System_Fan_amount = 0;
		}


		public static bool IsAdministrator()
		{
			WindowsIdentity identity = WindowsIdentity.GetCurrent();
			WindowsPrincipal principal = new WindowsPrincipal(identity);
			return principal.IsInRole(WindowsBuiltInRole.Administrator);
		}
		static Mutex mutex = new Mutex(true, "{8F6F0AC4-B9A1-45fd-A8CF-72F04E6BDE8F}");
		[STAThread]
		static void Main(string[] args)
        {
			if (mutex.WaitOne(TimeSpan.Zero, true))
			{
				if (!Program.IsAdministrator())
				{
					// Restart and run as admin
					var exeName = Process.GetCurrentProcess().MainModule.FileName;
					ProcessStartInfo startInfo = new ProcessStartInfo(exeName);
					startInfo.Verb = "runas";
					startInfo.Arguments = "restart";
					Process.Start(startInfo);
					System.Environment.Exit(1);
				}
				initilizeFanAmount();
				//
				while (true)
				{
					checkTemps();
					Thread.Sleep(1000);
				}
			}
			else
			{
				MessageBox.Show("Error: Only one instance at a time!");
			}
		}

		private static void checkTemps()
		{
			if (Registry.CheckLM("SOFTWARE\\OEM\\PredatorSense\\FanControl", "CurrentFanMode", 0u) == 0u)
			{
				Computer myComputer;
				double cpu = 0;
				double gpu = 0;
				myComputer = new Computer();
				myComputer.Open();
				myComputer.GPUEnabled = true;
				myComputer.CPUEnabled = true;

				foreach (var hardwareItem in myComputer.Hardware)
				{
					if (hardwareItem.HardwareType == HardwareType.GpuNvidia)
					{
						foreach (var sensor in hardwareItem.Sensors)
						{
							if (sensor.SensorType == SensorType.Temperature)
							{
								gpu = Convert.ToDouble(sensor.Value);
							}
						}
					}
					if (hardwareItem.HardwareType == HardwareType.CPU)
					{
						foreach (var sensor in hardwareItem.Sensors)
						{
							if (sensor.SensorType == SensorType.Temperature)
							{
								cpu = Convert.ToDouble(sensor.Value);
							}
						}
					}
				}

				if (gpu >= 78 && cpu > 80 && !overheating)
				{
					Set_WMI_FanMode(Fan_Mode_Type.Max);
					overheating = true;
					changedonce = false;
				}
				else if (gpu < 78 && cpu < 80 && !changedonce)
				{
					Set_WMI_FanMode(Fan_Mode_Type.Auto);
					overheating = false;
					changedonce = true;
				}
			}
		}

		private static void Set_WMI_FanMode(Fan_Mode_Type mode)
		{
			ulong num = 0uL;
			ulong num2 = 0uL;
			ulong input = 0uL;
			switch (mode)
			{
				case Fan_Mode_Type.Auto:
					{
						if (CPU_Fan_amount > 0)
						{
							num |= 1;
						}
						for (int m = 0; m < System_Fan_amount; m++)
						{
							num |= (ulong)(1L << m + 1);
						}
						for (int n = 0; n < GPU_Fan_amount; n++)
						{
							num |= (ulong)(1L << n + 3);
						}
						if (CPU_Fan_amount > 0)
						{
							num2 |= 1;
						}
						for (int num3 = 0; num3 < System_Fan_amount; num3++)
						{
							num2 |= (ulong)(1L << 2 * num3 + 2);
						}
						for (int num4 = 0; num4 < GPU_Fan_amount; num4++)
						{
							num2 |= (ulong)(1L << 2 * num4 + 6);
						}
						input = num | (num2 << 16);
						WMISetGamingFanGroupBehavior(input).Wait();
						break;
					}
				case Fan_Mode_Type.Max:
					{
						if (CPU_Fan_amount > 0)
						{
							num |= 1;
						}
						for (int i = 0; i < System_Fan_amount; i++)
						{
							num |= (ulong)(1L << i + 1);
						}
						for (int j = 0; j < GPU_Fan_amount; j++)
						{
							num |= (ulong)(1L << j + 3);

						}
						if (CPU_Fan_amount > 0)
						{
							num2 |= 2;
						}
						for (int k = 0; k < System_Fan_amount; k++)
						{
							num2 |= (ulong)(2L << 2 * k + 2);
						}
						for (int l = 0; l < GPU_Fan_amount; l++)
						{
							num2 |= (ulong)(2L << 2 * l + 6);

						}
						
						input = num | (num2 << 16);
						
						WMISetGamingFanGroupBehavior(input).Wait();
						break;
					}
				
			}
		}

		public static async Task<uint> WMISetGamingFanGroupBehavior(ulong intput)
		{
			try
			{
				NamedPipeClientStream cline_stream = new NamedPipeClientStream(".", "predatorsense_service_namedpipe", (PipeDirection)3);
				cline_stream.Connect();
				uint result = await Task.Run(delegate
				{
					IPCMethods.SendCommandByNamedPipe(cline_stream, 24, new object[1]
					{
						intput
					});
					((PipeStream)cline_stream).WaitForPipeDrain();
					byte[] array = new byte[9];
					((Stream)(object)cline_stream).Read(array, 0, array.Length);
					return BitConverter.ToUInt32(array, 5);
				}).ConfigureAwait(continueOnCapturedContext: false);
				((Stream)(object)cline_stream).Close();
				return result;
			}
			catch (Exception e)
			{
				return uint.MaxValue;
			}
		}

		public static async Task<ulong> WMIGetGamingFanGroupBehavior(uint intput)
		{
			try
			{
				NamedPipeClientStream cline_stream = new NamedPipeClientStream(".", "predatorsense_service_namedpipe", (PipeDirection)3);
				cline_stream.Connect();
				ulong result = await Task.Run(delegate
				{
					IPCMethods.SendCommandByNamedPipe(cline_stream, 25, new object[1]
					{
						intput
					});
					((PipeStream)cline_stream).WaitForPipeDrain();
					byte[] array = new byte[13];
					((Stream)(object)cline_stream).Read(array, 0, array.Length);
					return BitConverter.ToUInt64(array, 5);
				}).ConfigureAwait(continueOnCapturedContext: false);
				((Stream)(object)cline_stream).Close();
				return result;
			}
			catch (Exception)
			{
				return 4294967295uL;
			}
		}

		public static async Task<uint> WMISetGamingFanGroupSpeed(ulong intput)
		{
			try
			{
				NamedPipeClientStream cline_stream = new NamedPipeClientStream(".", "predatorsense_service_namedpipe", (PipeDirection)3);
				cline_stream.Connect();
				uint result = await Task.Run(delegate
				{
					IPCMethods.SendCommandByNamedPipe(cline_stream, 26, new object[1]
					{
						intput
					});
					((PipeStream)cline_stream).WaitForPipeDrain();
					byte[] array = new byte[9];
					((Stream)(object)cline_stream).Read(array, 0, array.Length);
					return BitConverter.ToUInt32(array, 5);
				}).ConfigureAwait(continueOnCapturedContext: false);
				((Stream)(object)cline_stream).Close();
				return result;
			}
			catch (Exception)
			{
				return uint.MaxValue;
			}
		}

		public static async Task<ulong> WMIGetGamingFanGroupSpeed(uint intput)
		{
			try
			{
				NamedPipeClientStream cline_stream = new NamedPipeClientStream(".", "predatorsense_service_namedpipe", (PipeDirection)3);
				cline_stream.Connect();
				ulong result = await Task.Run(delegate
				{
					IPCMethods.SendCommandByNamedPipe(cline_stream, 27, new object[1]
					{
						intput
					});
					((PipeStream)cline_stream).WaitForPipeDrain();
					byte[] array = new byte[13];
					((Stream)(object)cline_stream).Read(array, 0, array.Length);
					return BitConverter.ToUInt64(array, 5);
				}).ConfigureAwait(continueOnCapturedContext: false);
				((Stream)(object)cline_stream).Close();
				return result;
			}
			catch (Exception)
			{
				return 4294967295uL;
			}
		}
	}
}

