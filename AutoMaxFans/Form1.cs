using OpenHardwareMonitor.Hardware;
using System;
using System.IO;
using System.IO.Pipes;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Forms;
using System.Xml.Linq;
using System.Xml.XPath;
using TsDotNetLib;

namespace AutoMaxFans
{
    public partial class Form1 : Form
    {
		private static bool cpuchangedone = false;
		private static bool cpuoverheating = false;

		private string _filename = Path.Combine(Application.StartupPath, "settings.xml");

		private static bool gpuoverheating = false;
		
		private int gputhreshold;
		private int cputhreshold;
		private bool gpuenabled;
		private bool cpuenabled;

		private static bool gpuchangedone = false;
		private static System.Timers.Timer aTimer;
		private XDocument _doc;
		private int counter = 0;


		public Form1()
        {
			
			InitializeComponent();
			try
			{
				_doc = XDocument.Load(_filename);
				_doc.Save(_filename);
				XElement node = _doc.XPathSelectElement("//Threshold/GPU[1]");
				numericUpDown1.Value = Convert.ToDecimal(node.Value);
				node = _doc.XPathSelectElement("//Threshold/CPU[1]");
				numericUpDown2.Value = Convert.ToDecimal(node.Value);
				set_coolboost_state(state: true);
				
				aTimer = new System.Timers.Timer(1000);
				aTimer.Elapsed += runAutoFans;
				aTimer.AutoReset = true;
				aTimer.Enabled = true;
				notifyIcon1.BalloonTipText = "The app has been minimized to the system tray.";
				notifyIcon1.Text = "Auto Max Fans";
			}
			catch (Exception e)
			{
				MessageBox.Show(e.ToString());
			}

		}

		private void Form1_Show(object sender, EventArgs e)
        {
			try
			{
				string[] args = Environment.GetCommandLineArgs();

				if (args[1] == "-m") this.WindowState = FormWindowState.Minimized;
			}
			catch { }

		}

		private void Form1_Resize(object sender, EventArgs e)
		{
			if (FormWindowState.Minimized == this.WindowState)
			{
				notifyIcon1.Visible = true;
				notifyIcon1.ShowBalloonTip(500);
				this.Hide();
			}

			else if (FormWindowState.Normal == this.WindowState)
			{
				notifyIcon1.Visible = false;
			}
		}
		
		private async void Form1_FormClosing(object sender, FormClosingEventArgs e)
		{
			if (Registry.CheckLM("SOFTWARE\\OEM\\PredatorSense\\FanControl", "CurrentFanMode", 0u) == 0u)
            {
				e.Cancel = true;
				try
				{
					_doc.XPathSelectElement("//Threshold").RemoveAll();
					_doc.XPathSelectElement("//Threshold").Add(new XElement("GPU", numericUpDown1.Value));
					_doc.XPathSelectElement("//Threshold").Add(new XElement("CPU", numericUpDown2.Value));
					_doc.Save(_filename);
					await setCpuFan(0);
					await setGpuFan(0);		
				}
                catch (Exception e2)
                {
					MessageBox.Show(e2.ToString());
				}
				Environment.Exit(1);
			}
		}

		private void runAutoFans(Object source, ElapsedEventArgs e)
		{
			checkTemps();
		}

		private async void checkTemps()
		{
			
			if (Registry.CheckLM("SOFTWARE\\OEM\\PredatorSense\\FanControl", "CurrentFanMode", 0u) == 0u && checkBox3.Checked)
			{
				try
				{
					gputhreshold = (int)numericUpDown1.Value;
					cputhreshold = (int)numericUpDown2.Value;
					gpuenabled = checkBox1.Checked;
					cpuenabled = checkBox2.Checked;

					Computer myComputer;
					int cpu = 0;
					int gpu = 0;
					myComputer = new Computer();
					myComputer.Open();
					myComputer.GPUEnabled = true;
					myComputer.CPUEnabled = true;

					foreach (var hardwareItem in myComputer.Hardware)
					{
						if (hardwareItem.HardwareType == HardwareType.GpuNvidia && gpuenabled)
						{
							foreach (var sensor in hardwareItem.Sensors)
							{
								if (sensor.SensorType == SensorType.Temperature)
								{
									if (sensor.Value == null) continue;
									gpu = Convert.ToInt32(sensor.Value);
								}
							}
						}
						if (hardwareItem.HardwareType == HardwareType.CPU && cpuenabled)
						{
							foreach (var sensor in hardwareItem.Sensors)
							{
								if (sensor.SensorType == SensorType.Temperature)
								{
									cpu = Convert.ToInt32(sensor.Value);
								}
							}
						}
					}
					if (gpuenabled && gpu != 0)
					{
						if (gpu >= gputhreshold && !gpuoverheating)
						{
							await setGpuFan(100);
							gpuoverheating = true;
							gpuchangedone = false;
						}
						else if (gpu < gputhreshold && !gpuchangedone)
						{
							await setGpuFan(0);
							gpuoverheating = false;
							gpuchangedone = true;
						}
					}
					if (cpuenabled)
					{
						Console.WriteLine(counter);
						if (cpu >= cputhreshold)
						{
							counter++;
							if (!cpuoverheating)
							{
								if (gpuoverheating)
								{
									await setCpuFan(100);
									cpuoverheating = true;
									cpuchangedone = false;
								}
								else if(counter >= 10)
                                {
									await setCpuFan(100);
									cpuoverheating = true;
									cpuchangedone = false;
								}
							}
						}
						else if (cpu < cputhreshold)
						{
							if (!cpuchangedone)
							{
								await setCpuFan(0);
								cpuoverheating = false;
								cpuchangedone = true;
							}
							counter = 0;
						}
					}
				}
				catch
                {
					return;
                }
			}
			else
            {
				cpuoverheating = false;
				cpuchangedone = true;
				gpuoverheating = false;
				gpuchangedone = true;
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
			catch (Exception)
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

		public static async Task<bool> set_single_custom_fan_state(bool auto, ulong percentage, string fan_group_type)
		{
			bool ret = true;
			ulong num = 0uL;
			try
			{
				switch (fan_group_type)
				{
					case "CPU":
						if (!auto)
						{
							num |= 0x30001;
							await WMISetGamingFanGroupBehavior(num);
							await WMISetGamingFanGroupSpeed(1 | (percentage << 8));
							return ret;
						}
						num |= 0x10001;
						await WMISetGamingFanGroupBehavior(num);
						return ret;
					case "GPU":
						if (!auto)
						{
							num |= 0xC00008;
							await WMISetGamingFanGroupBehavior(num);
							await WMISetGamingFanGroupSpeed(4 | (percentage << 8));
							
							return ret;
						}
						num |= 0x400008;
						await WMISetGamingFanGroupBehavior(num);
						return ret;
					default:
						return ret;
				}
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
				return false;
			}
		}

		public static async Task<ulong> WMIGetFunction(uint intput)
		{
			try
			{
				NamedPipeClientStream cline_stream = new NamedPipeClientStream(".", "predatorsense_service_namedpipe", (PipeDirection)3);
				cline_stream.Connect();
				ulong result = await Task.Run(delegate
				{
					IPCMethods.SendCommandByNamedPipe(cline_stream, 9, new object[1]
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

		public static async Task<uint> WMISetFunction(ulong intput)
		{
			try
			{
				NamedPipeClientStream cline_stream = new NamedPipeClientStream(".", "predatorsense_service_namedpipe", (PipeDirection)3);
				cline_stream.Connect();
				uint result = await Task.Run(delegate
				{
					IPCMethods.SendCommandByNamedPipe(cline_stream, 10, new object[1]
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


		private async Task<bool> setGpuFan(int percentage)
        {

			if(percentage == 0)
            {
				await set_single_custom_fan_state(true, 100u, "GPU");
				if (get_coolboost_state() == false) set_coolboost_state(state: true);
			}
			
			if (!cpuoverheating) // Does not work normally. This is the only workaround for now
			{
				await set_single_custom_fan_state(false, 100u, "CPU");
				await set_single_custom_fan_state(false, (ulong) percentage, "GPU");
				await set_single_custom_fan_state(true, 100u, "CPU");
			}
			else await set_single_custom_fan_state(false, (ulong) percentage, "GPU");
			return true;
        }

		private async Task<bool> setCpuFan(int percentage)
		{
			if (percentage == 100)
            {
				if (!gpuoverheating) // Does not work normally. This is the only workaround for now
				{
					await set_single_custom_fan_state(false, 100u, "GPU");
					await set_single_custom_fan_state(false, 100u, "CPU");
					await set_single_custom_fan_state(true, 100u, "GPU");
				}
				else await set_single_custom_fan_state(false, 100u, "CPU");
			}
			else if(percentage == 0)
            {
				await set_single_custom_fan_state(true, 100u, "CPU");
				if (get_coolboost_state() == false) set_coolboost_state(state: true);
			}
			return true;
        }

        private async void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
			if (!checkBox1.Checked)
			{
				if (Registry.CheckLM("SOFTWARE\\OEM\\PredatorSense\\FanControl", "CurrentFanMode", 0u) == 0u) await setGpuFan(0);
				gpuoverheating = false;
				gpuchangedone = true;
			}
		}

        private async void checkBox2_CheckedChanged(object sender, EventArgs e)
        {
			if (!checkBox2.Checked)
			{
				if (Registry.CheckLM("SOFTWARE\\OEM\\PredatorSense\\FanControl", "CurrentFanMode", 0u) == 0u) await setCpuFan(0);
				cpuoverheating = false;
				cpuchangedone = true;
			}
		}

        private void notifyIcon1_MouseDoubleClick(object sender, MouseEventArgs e)
        {
			this.Show();
			this.WindowState = FormWindowState.Normal;
		}

		public static bool set_coolboost_state(bool state)
		{
			if ((WMISetFunction(7uL | (ulong)((state ? 1 : 0) << 16)).GetAwaiter().GetResult() & 0xFF) == 0)
			{
				return true;
			}
			return false;
		}
		private static bool get_coolboost_state()
		{
			ulong result = WMIGetFunction(519u).GetAwaiter().GetResult();
			if ((result & 0xFF) == 0L && ((result >> 8) & 0xFF) == 1)
			{
				return true;
			}
			return false;
		}

		private async void checkBox3_CheckedChanged(object sender, EventArgs e)
        {
			if (!checkBox3.Checked)
			{
				checkBox1.Enabled = false;
				checkBox2.Enabled = false;
				numericUpDown1.Enabled = false;
				numericUpDown2.Enabled = false;
				if (Registry.CheckLM("SOFTWARE\\OEM\\PredatorSense\\FanControl", "CurrentFanMode", 0u) == 0u)
				{
					await setCpuFan(0);
					await setGpuFan(0);
				}
			}
			else
			{
				checkBox1.Enabled = true;
				checkBox2.Enabled = true;
				numericUpDown1.Enabled = true;
				numericUpDown2.Enabled = true;
			}
        }
    }
}
