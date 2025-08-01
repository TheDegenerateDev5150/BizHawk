using System.Drawing;

namespace BizHawk.Client.EmuHawk
{
	public partial class TAStudio
	{
		// Everything here is currently for Lua
		public Func<int, string, Color?> QueryItemBgColorCallback { get; set; }
		public Func<int, string, string> QueryItemTextCallback { get; set; }
		public Func<int, string, Bitmap> QueryItemIconCallback { get; set; }

		public Action<int> GreenzoneInvalidatedCallback { get; set; }
		public Action<int> BranchLoadedCallback { get; set; }
		public Action<int> BranchSavedCallback { get; set; }
		public Action<int> BranchRemovedCallback { get; set; }
	}
}
