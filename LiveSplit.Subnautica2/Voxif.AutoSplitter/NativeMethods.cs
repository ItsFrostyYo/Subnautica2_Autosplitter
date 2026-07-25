using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Voxif.AutoSplitter {
    public static class NativeMethods {

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int SendMessage(IntPtr hWnd, int wMsg, bool wParam, int lParam);

        private const int WM_SETREDRAW = 11;
        public static void EnableDraw(IntPtr ptr, bool value) {
            SendMessage(ptr, WM_SETREDRAW, value, 0);
        }


        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint wMsg, IntPtr wParam, ref TVITEM lParam);
        
        private const int TVIF_STATE = 0x8;
        private const int TVIS_STATEIMAGEMASK = 0xF000;
        private const int TV_FIRST = 0x1100;
        private const int TVM_SETITEM = TV_FIRST + 63;

        [StructLayout(LayoutKind.Sequential)]
        private struct TVITEM {
            public int mask;
            public IntPtr hItem;
            public int state;
            public int stateMask;
            public IntPtr pszText;
            public int cchTextMax;
            public int iImage;
            public int iSelectedImage;
            public int cChildren;
            public IntPtr lParam;
        }

        public static void HideCheckBox(TreeNode node) {
            TVITEM tvi = new TVITEM {
                hItem = node.Handle,
                mask = TVIF_STATE,
                stateMask = TVIS_STATEIMAGEMASK,
                state = 0
            };
            SendMessage(node.TreeView.Handle, TVM_SETITEM, default, ref tvi);
        }


        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int wMsg, IntPtr wParam, IntPtr lParam);

        private const int TVM_SETEXTENDEDSTYLE = 0x1100 + 44;
        private const int TVS_EX_DOUBLEBUFFER = 0x0004;
        public static void CreateHandler(IntPtr ptr) {
            SendMessage(ptr, TVM_SETEXTENDEDSTYLE, (IntPtr)TVS_EX_DOUBLEBUFFER, (IntPtr)TVS_EX_DOUBLEBUFFER);
        }
    }
}