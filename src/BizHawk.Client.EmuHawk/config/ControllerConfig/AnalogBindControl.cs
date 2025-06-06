using System.Windows.Forms;

using BizHawk.Client.Common;

namespace BizHawk.Client.EmuHawk
{
	public partial class AnalogBindControl : UserControl
	{
		private AnalogBindControl()
		{
			InitializeComponent();
		}

		public AnalogBindControl(string buttonName, AnalogBind bind)
			: this()
		{
			_bind = bind;
			ButtonName = buttonName;
			labelButtonName.Text = buttonName;
			trackBarSensitivity.Value = (int) Math.Round(bind.Mult * 20.0);
			trackBarDeadzone.Value = (int) Math.Round(bind.Deadzone * 50.0);
			TrackBarSensitivity_ValueChanged(null, null);
			TrackBarDeadzone_ValueChanged(null, null);
			textBox1.Text = bind.Value;
		}

		public string ButtonName { get; }
		public AnalogBind Bind => _bind;

		private AnalogBind _bind;
		private bool _listening;

		private void Timer1_Tick(object sender, EventArgs e)
		{
			string bindValue = Input.Instance.GetNextAxisEvent();
			if (bindValue != null)
			{
				timer1.Stop();
				_listening = false;
				buttonBind.Text = "Bind!";
				Input.Instance.StopListeningForAxisEvents();
				// rebinding the same mouse axis toggles between relative and absolute
				const string AXIS_MX = "WMouse X";
				const string AXIS_MRX = "RMouse X";
				const string AXIS_MY = "WMouse Y";
				const string AXIS_MRY = "RMouse Y";
				var oldBind = _bind.Value;
				if (bindValue is AXIS_MX)
				{
					if (oldBind is AXIS_MX) bindValue = AXIS_MRX;
					else if (oldBind is AXIS_MRX) bindValue = AXIS_MX;
				}
				else if (bindValue is AXIS_MY)
				{
					if (oldBind is AXIS_MY) bindValue = AXIS_MRY;
					else if (oldBind is AXIS_MRY) bindValue = AXIS_MY;
				}
				textBox1.Text = _bind.Value = bindValue;
			}
		}

		private void ButtonBind_Click(object sender, EventArgs e)
		{
			if (_listening)
			{
				timer1.Stop();
				_listening = false;
				buttonBind.Text = "Bind!";
				Input.Instance.StopListeningForAxisEvents();
			}
			else
			{
				Input.Instance.StartListeningForAxisEvents();
				_listening = true;
				buttonBind.Text = "Cancel!";
				timer1.Start();
			}
		}

		private void TrackBarSensitivity_ValueChanged(object sender, EventArgs e)
		{
			_bind.Mult = trackBarSensitivity.Value / 20.0f;
			labelSensitivity.Text = $"Sensitivity: {_bind.Mult:P0}";
		}

		private void TrackBarDeadzone_ValueChanged(object sender, EventArgs e)
		{
			_bind.Deadzone = trackBarDeadzone.Value / 50.0f;
			labelDeadzone.Text = $"Deadzone: {_bind.Deadzone:P0}";
		}

		private void ButtonFlip_Click(object sender, EventArgs e)
		{
			trackBarSensitivity.Value *= -1;
		}

		public void Unbind_Click(object sender, EventArgs e)
		{
			_bind.Value = "";
			textBox1.Text = "";
		}
	}
}
