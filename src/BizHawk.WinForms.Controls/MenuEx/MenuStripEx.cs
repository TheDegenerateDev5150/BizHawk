using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

using BizHawk.Common;

namespace BizHawk.WinForms.Controls
{
	/// <summary>
	/// This class adds on to the functionality provided in <see cref="MenuStrip"/>.
	/// </summary>
	public class MenuStripEx : MenuStrip
	{
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public new Size Size => base.Size;

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public new Point Location => new Point(0, 0);

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public new string Text => "";

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public new string Name
			=> Util.GetRandomUUIDStr();

		protected override void WndProc(ref Message m)
		{
			base.WndProc(ref m);
			if (m.Msg == NativeConstants.WM_MOUSEACTIVATE
				&& m.Result == (IntPtr)NativeConstants.MA_ACTIVATEANDEAT)
			{
				m.Result = (IntPtr)NativeConstants.MA_ACTIVATE;
			}
		}
	}
}
