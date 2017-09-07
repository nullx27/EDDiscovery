﻿/*
 * Copyright © 2016 - 2017 EDDiscovery development team
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not use this
 * file except in compliance with the License. You may obtain a copy of the License at
 *
 * http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing, software distributed under
 * the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF
 * ANY KIND, either express or implied. See the License for the specific language
 * governing permissions and limitations under the License.
 * 
 * EDDiscovery is not affiliated with Frontier Developments plc.
 */
using EDDiscovery.UserControls;
using BaseUtils.Win32Constants;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using EliteDangerousCore.DB;

namespace EDDiscovery.Forms
{
    public partial class UserControlForm : ExtendedControls.DraggableForm
    {
        public UserControlCommonBase UserControl;
        public bool isloaded = false;
        public bool norepositionwindow = false;
        public bool istemporaryresized = false;

        public bool istransparent = false;          // we are in transparent mode (but may be showing due to inpanelshow)
        public bool clickthruwhentransparent = true;   // click thru when transparent..

        Keys clickthrukey = Keys.Shift;

        public bool displayTitle = true;            // we are displaying the title
        public string dbrefname;
        public string wintitle;

        private bool inpanelshow = false;       // if we are in a panel show when we were transparent
        private bool defwindowsborder;
        private bool curwindowsborder;          // applied setting
        private Color transparencycolor = Color.Transparent;
        private Color beforetransparency = Color.Transparent;
        private Color tkey = Color.Transparent;
        private Color labelnormalcolour = Color.Transparent, labeltransparentcolour = Color.Transparent;

        private Timer timer = new Timer();      // timer to monitor for entry into form when transparent.. only sane way in forms
        private bool deftopmost, deftransparent;
        private Size normalsize;

        public bool IsTransparencySupported { get { return !transparencycolor.IsFullyTransparent(); } }

        public UserControlForm()
        {
            InitializeComponent();

            timer.Interval = 500;
            timer.Tick += CheckMouse;
        }

        #region Public Interface

        public void Init(EDDiscovery.UserControls.UserControlCommonBase c, string title, bool winborder, string rf, bool deftopmostp = false, bool defwindowintaskbar = true)
        {
            UserControl = c;
            c.Dock = DockStyle.None;
            c.Location = new Point(0, 10);
            c.Size = new Size(200, 200);
            this.Controls.Add(c);

            transparencycolor = c.ColorTransparency;

            wintitle = label_index.Text = this.Text = title;            // label index always contains the wintitle, but may not be shown

            curwindowsborder = defwindowsborder = winborder;
            dbrefname = "PopUpForm" + rf;
            this.Name = rf; 
            deftopmost = deftopmostp;
            deftransparent = false;

            labelControlText.Text = "";                                 // always starts blank..

            this.ShowInTaskbar = SQLiteDBClass.GetSettingBool(dbrefname + "Taskbar", defwindowintaskbar);

            displayTitle = SQLiteDBClass.GetSettingBool(dbrefname + "ShowTitle", true);

            UpdateControls();

            Invalidate();
        }

        public void InitForTransparency(bool deftransparentp, Color labeln, Color labelt)
        {
            deftransparent = deftransparentp;
            labelnormalcolour = labeln;
            labeltransparentcolour = labelt;
        }

        public void SetControlText(string text)
        {
            labelControlText.Location = new Point(label_index.Location.X + label_index.Width + 16, labelControlText.Location.Y);
            labelControlText.Text = text;
            this.Text = wintitle + " " + text;
        }

        public void SetTransparency(bool t)
        {
            if (IsTransparencySupported)
            {
                istransparent = t;
                UpdateTransparency();
                SQLiteDBClass.PutSettingBool(dbrefname + "Transparent", istransparent);
            }
        }

        public void SetClickThruWhenTransparent(bool t)
        {
            if (IsTransparencySupported)
            {
                clickthruwhentransparent = t;
                UpdateTransparency();
                SQLiteDBClass.PutSettingBool(dbrefname + "TransparentClickThru", clickthruwhentransparent);
            }

        }

        public void SetShowTitleInTransparency(bool t)
        {
            displayTitle = t;
            UpdateControls();
            SQLiteDBClass.PutSettingBool(dbrefname + "ShowTitle", displayTitle);
        }

        public void SetTopMost(bool t)
        {
            TopMost = t;
            UpdateControls();
            SQLiteDBClass.PutSettingBool(dbrefname + "TopMost", TopMost);
        }

        public void SetShowInTaskBar(bool t)
        {
            this.ShowInTaskbar = t;
            UpdateControls();
            SQLiteDBClass.PutSettingBool(dbrefname + "Taskbar", t);
        }

        public UserControlCommonBase FindUserControl(Type c)
        {
            if (UserControl != null && UserControl.GetType().Equals(c))
                return UserControl;
            else
                return null;
        }

        #endregion

        #region View Implementation

        private void UpdateTransparency()
        {
            curwindowsborder = (!istransparent && defwindowsborder);    // we have a border if not transparent and we have a def border
            bool showtransparent = istransparent && !inpanelshow;           // are we transparent..  must not be in panel show

            if (beforetransparency.IsFullyTransparent())        // record colour before transparency, dynamically
            {
                beforetransparency = this.BackColor;
                tkey = this.TransparencyKey;
            }

            UpdateControls();

            this.TransparencyKey = (showtransparent) ? transparencycolor : tkey;        
            Color togo = (showtransparent) ? transparencycolor : beforetransparency;

            this.BackColor = togo;
            statusStripBottom.BackColor = togo;
            panel_taskbaricon.BackColor = panel_transparent.BackColor = panel_close.BackColor =
                    panel_minimize.BackColor = panel_ontop.BackColor = panel_showtitle.BackColor = panelTop.BackColor = togo;

            System.Diagnostics.Debug.Assert(!labeltransparentcolour.IsFullyTransparent());
            label_index.ForeColor = labelControlText.ForeColor = (istransparent) ? labeltransparentcolour : labelnormalcolour;

            UserControl.SetTransparency(showtransparent, togo);
            PerformLayout();

            if (showtransparent || inpanelshow)     // timer needed if transparent, or if in panel show
                timer.Start();
            else
                timer.Stop();


            UpdateClickThru();
        }

        void UpdateClickThru()
        {
            int cur = BaseUtils.Win32.UnsafeNativeMethods.GetWindowLong(this.Handle, BaseUtils.Win32.UnsafeNativeMethods.GWL.ExStyle);

            if (istransparent && !inpanelshow && clickthruwhentransparent)      // if transparent, and we want click thru when transparent
                cur = cur | WS_EX.TRANSPARENT | WS_EX.LAYERED;
            else
                cur = cur & ~(WS_EX.TRANSPARENT | WS_EX.LAYERED);

            BaseUtils.Win32.UnsafeNativeMethods.SetWindowLong(this.Handle, BaseUtils.Win32.UnsafeNativeMethods.GWL.ExStyle , cur);
        }

        private void UpdateControls()
        {
            bool transparent = istransparent && !inpanelshow;           // are we transparent..

            FormBorderStyle = curwindowsborder ? FormBorderStyle.Sizable : FormBorderStyle.None;
            panelTop.Visible = !curwindowsborder;       // this also has the effect of removing the label_ and panel_ buttons

            statusStripBottom.Visible = !transparent && !curwindowsborder;      // status strip on, when not transparent, and when we don't have border

            panel_taskbaricon.Visible = panel_close.Visible = panel_minimize.Visible = panel_ontop.Visible = panel_showtitle.Visible = !transparent;

            panel_transparent.Visible = IsTransparencySupported && !transparent;
            panel_showtitle.Visible = IsTransparencySupported && !transparent;
            panel_transparent.ImageSelected = (istransparent && clickthruwhentransparent) ? ExtendedControls.DrawnPanel.ImageType.TransparentClickThru : ((istransparent) ? ExtendedControls.DrawnPanel.ImageType.Transparent : ExtendedControls.DrawnPanel.ImageType.NotTransparent);

            label_index.Visible = labelControlText.Visible = (displayTitle || !transparent);   //  titles are on, or transparent is off

            panel_taskbaricon.ImageSelected = this.ShowInTaskbar ? ExtendedControls.DrawnPanel.ImageType.WindowInTaskBar : ExtendedControls.DrawnPanel.ImageType.WindowNotInTaskBar;
            panel_showtitle.ImageSelected = displayTitle ? ExtendedControls.DrawnPanel.ImageType.Captioned : ExtendedControls.DrawnPanel.ImageType.NotCaptioned;

            fucking buttons don't work.. fix
        }

        private void UserControlForm_Layout(object sender, LayoutEventArgs e)
        {
            if (UserControl != null)
            {
                UserControl.Location = new Point(3, curwindowsborder ? 2 : panelTop.Location.Y + panelTop.Height);
                UserControl.Size = new Size(ClientRectangle.Width - 6, ClientRectangle.Height - UserControl.Location.Y - (curwindowsborder ? 0 : statusStripBottom.Height));
            }
        }

        private void UserControlForm_Shown(object sender, EventArgs e)          // as launched, it may not be in front (as its launched from a control).. bring to front
        {
            this.BringToFront();

            bool tr = SQLiteDBClass.GetSettingBool(dbrefname + "Transparent", deftransparent);
            if (tr && IsTransparencySupported)     // the check is for paranoia
            {
                SetTransparency(true);      // only call if transparent.. may not be fully set up so don't merge with above
                why the green flash
                // clickthruwhentransparent = SQLiteDBClass.GetSettingBool(dbrefname + "TransparentClickThru", false);
            }

            SetTopMost(SQLiteDBClass.GetSettingBool(dbrefname + "TopMost", deftopmost));

            var top = SQLiteDBClass.GetSettingInt(dbrefname + "Top", -999);
            System.Diagnostics.Debug.WriteLine("Position Top is {0} {1}", dbrefname, top);

            if (top != -999 && norepositionwindow == false)
            {
                var left = SQLiteDBClass.GetSettingInt(dbrefname + "Left", 0);
                var height = SQLiteDBClass.GetSettingInt(dbrefname + "Height", 800);
                var width = SQLiteDBClass.GetSettingInt(dbrefname + "Width", 800);

                System.Diagnostics.Debug.WriteLine("Position {0} {1} {2} {3} {4}", dbrefname, top, left, width, height);
                // Adjust so window fits on screen; just in case user unplugged a monitor or something

                var screen = SystemInformation.VirtualScreen;
                if (height > screen.Height) height = screen.Height;
                if (top + height > screen.Height + screen.Top) top = screen.Height + screen.Top - height;
                if (width > screen.Width) width = screen.Width;
                if (left + width > screen.Width + screen.Left) left = screen.Width + screen.Left - width;
                if (top < screen.Top) top = screen.Top;
                if (left < screen.Left) left = screen.Left;

                this.Top = top;
                this.Left = left;
                this.Height = height;
                this.Width = width;

                this.CreateParams.X = this.Left;
                this.CreateParams.Y = this.Top;
                this.StartPosition = FormStartPosition.Manual;
            }

            if (UserControl != null)
                UserControl.LoadLayout();

            isloaded = true;

            UpdateClickThru();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
        }

        private void UserControlForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            isloaded = false;

            if (UserControl != null)
                UserControl.Closing();

            Size winsize = (istemporaryresized) ? normalsize : this.Size;
            SQLiteDBClass.PutSettingInt(dbrefname + "Width", winsize.Width);
            SQLiteDBClass.PutSettingInt(dbrefname + "Height", winsize.Height);
            SQLiteDBClass.PutSettingInt(dbrefname + "Top", this.Top);
            SQLiteDBClass.PutSettingInt(dbrefname + "Left", this.Left);
            System.Diagnostics.Debug.WriteLine("Save Position {0} {1} {2} {3} {4}", dbrefname , Top, Left, winsize.Width, winsize.Height);
        }

        #endregion

        #region Clicks

        private void panel_close_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void panel_minimize_Click(object sender, EventArgs e)
        {
            this.WindowState = FormWindowState.Minimized;
        }

        private void panel_ontop_Click(object sender, EventArgs e)
        {
            SetTopMost(!TopMost);
        }

        private void panel_transparency_Click(object sender, EventArgs e)       // only works if transparency is supported
        {
            inpanelshow = true;
            SetTransparency(!istransparent);
            //if (clickthruwhentransparent)     // if set, go to off/off
            //{
            //    clickthruwhentransparent = false;
            //    SetTransparency(false);
            //}
            //else if (istransparent)           // if this is set, go to on/on
            //{
            //    SetClickThruWhenTransparent(true);
            //}
            //else
            //{
            //    SetTransparency(true);     // else its transparency off, goto on/off
            //}
        }

        private void panel_taskbaricon_Click(object sender, EventArgs e)
        {
            SetShowInTaskBar(!this.ShowInTaskbar);
        }

        private void panel_showtitle_Click(object sender, EventArgs e)
        {
            SetShowTitleInTransparency(!displayTitle);
        }

        private void CheckMouse(object sender, EventArgs e)     // best way of knowing your inside the client.. using mouseleave/enter with transparency does not work..
        {
            if (isloaded)
            {
                //System.Diagnostics.Debug.WriteLine(Environment.TickCount + " Tick" + istransparent + " " + inpanelshow);
                if (ClientRectangle.Contains(this.PointToClient(MousePosition)) )
                {
                    System.Diagnostics.Debug.WriteLine(Environment.TickCount + "In area");

                    if (Control.ModifierKeys.HasFlag(clickthrukey) || clickthrukey == Keys.None)
                    {
                        if (!inpanelshow)
                        {
                            inpanelshow = true;
                            UpdateTransparency();
                        }
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine(Environment.TickCount + "Out of area");

                    if (inpanelshow)
                    {
                        inpanelshow = false;
                        UpdateTransparency();
                    }
                }
            }
        }

        #endregion

        #region Resizing

        public void RequestTemporaryMinimiumSize(Size w)            // Size w is the client area used by the UserControl..
        {
            int width = ClientRectangle.Width < w.Width ? (w.Width - ClientRectangle.Width) : 0;
            int height = ClientRectangle.Height < w.Height ? (w.Height - ClientRectangle.Height) : 0;

            RequestTemporaryResizeExpand(new Size(width, height));
        }

        public void RequestTemporaryResizeExpand(Size w)            // Size w is the client area above
        {
            if (w.Width != 0 || w.Height != 0)
                RequestTemporaryResize(new Size(ClientRectangle.Width + w.Width, ClientRectangle.Height + w.Height));
        }

        public void RequestTemporaryResize(Size w)                  // Size w is the client area above
        {
            if (!istemporaryresized)
            {
                normalsize = this.Size;
                istemporaryresized = true;                          // we are setting window size, so we need to consider the bounds around the window
                int widthoutsideclient = (Bounds.Size.Width - ClientRectangle.Width);
                int heightoutsideclient = (Bounds.Size.Height - ClientRectangle.Height);
                int heightlosttoothercontrols = UserControl.Location.Y + statusStripBottom.Height; // and the area used by the other bits of the window outside the user control
                this.Size = new Size(w.Width + widthoutsideclient, w.Height + heightlosttoothercontrols + heightoutsideclient);
            }
        }

        public void RevertToNormalSize()
        {
            if (istemporaryresized)
            {
                this.Size = normalsize;
                istemporaryresized = false;
            }
        }

        #endregion

        #region Low level Wndproc

        protected const int SC_TRANSPARENT = 0x0020;    // Different from SmartSysMenuForm's SC_OPACITYSUBMENU.
        protected const int SC_TASKBAR = 0x0021;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            // Windows title-bar context menu manipulation (2000 and above)
            if (Environment.OSVersion.Platform == PlatformID.Win32NT && Environment.OSVersion.Version.Major >= 5)
            {
                // Get a handle to a copy of this form's system (window) menu
                IntPtr hSysMenu = BaseUtils.Win32.UnsafeNativeMethods.GetSystemMenu(Handle, false);
                if (hSysMenu != IntPtr.Zero)
                {
                    // Add the About menu item
                    BaseUtils.Win32.UnsafeNativeMethods.AppendMenu(hSysMenu, MF.STRING, SC_TRANSPARENT, "&Transparent");
                    BaseUtils.Win32.UnsafeNativeMethods.AppendMenu(hSysMenu, MF.STRING, SC_TASKBAR, "Show icon in Task&Bar for window");
                }
            }
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case WM.INITMENU:
                    {
                        base.WndProc(ref m);
                        if (m.WParam != IntPtr.Zero && Environment.OSVersion.Platform == PlatformID.Win32NT && IsHandleCreated)
                        {
                            BaseUtils.Win32.UnsafeNativeMethods.EnableMenuItem(m.WParam, SC_TRANSPARENT, MF.BYCOMMAND | (IsTransparencySupported ? MF.ENABLED : MF.GRAYED));
                            BaseUtils.Win32.UnsafeNativeMethods.ModifyMenu(m.WParam, SC_TASKBAR, MF.BYCOMMAND | (ShowInTaskbar ? MF.CHECKED : MF.UNCHECKED), SC_TASKBAR, "Show icon in Task&Bar for window");
                            m.Result = IntPtr.Zero;
                        }
                        return;
                    }
                case WM.SYSCOMMAND:
                    {
                        if (m.WParam == (IntPtr)SC_TASKBAR)
                        {
                            panel_taskbaricon_Click(panel_taskbaricon, EventArgs.Empty);
                        }
                        else if (m.WParam == (IntPtr)SC_TRANSPARENT)
                        {
                            if (IsTransparencySupported)
                                panel_transparency_Click(panel_transparent, EventArgs.Empty);
                            else
                                ExtendedControls.MessageBoxTheme.Show("This panel does not support transparency");
                        }
                        else
                            break;

                        m.Result = IntPtr.Zero;
                        return;
                    }
            }
            base.WndProc(ref m);
        }

        private void panelTop_MouseDown(object sender, MouseEventArgs e)
        {
            OnCaptionMouseDown((Control)sender, e);
        }

        private void panelTop_MouseUp(object sender, MouseEventArgs e)
        {
            OnCaptionMouseUp((Control)sender, e);
        }

        #endregion
    }

    public class UserControlFormList
    {
        private List<UserControlForm> tabforms;
        EDDiscoveryForm discoveryform;

        public int Count { get { return tabforms.Count; } }

        public UserControlFormList(EDDiscoveryForm ed)
        {
            tabforms = new List<UserControlForm>();
            discoveryform = ed;
        }

        public UserControlForm this[int i] { get { return tabforms[i]; } }

        public UserControlForm Get(string name)
        {
            foreach (UserControlForm u in tabforms)
            {
                if (u.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase))
                    return u;
            }

            foreach (UserControlForm u in tabforms)
            {
                if (u.Name.StartsWith(name, StringComparison.InvariantCultureIgnoreCase))
                    return u;
            }

            return null;
        }

        public UserControlForm NewForm(bool noreposition)
        {
            UserControlForm tcf = new UserControlForm();
            tabforms.Add(tcf);

            tcf.norepositionwindow = noreposition;
            tcf.FormClosed += FormClosed;
            return tcf;
        }

        private void FormClosed(Object sender, FormClosedEventArgs e)
        {
            UserControlForm tcf = (UserControlForm)sender;
            tabforms.Remove(tcf);
            discoveryform.ActionRun(Actions.ActionEventEDList.onPopDown, null, new Conditions.ConditionVariables(new string[] { "PopOutName", tcf.dbrefname.Substring(9), "PopOutTitle", tcf.wintitle }));
        }

        public List<UserControlCommonBase> GetListOfControls(Type c)
        {
            List<UserControlCommonBase> lc = new List<UserControlCommonBase>();

            foreach (UserControlForm tcf in tabforms)
            {
                if (tcf.isloaded)
                {
                    UserControlCommonBase uc = tcf.FindUserControl(c);
                    if (uc != null)
                        lc.Add(uc);
                }
            }

            return lc;
        }

        public int CountOf(Type c)
        {
            int count = 0;

            foreach (UserControlForm tcf in tabforms)
            {
                if (tcf.FindUserControl(c) != null)
                    count++;
            }

            return count;
        }

        public void ShowAllInTaskBar()
        {
            foreach (UserControlForm ucf in tabforms)
            {
                if (ucf.isloaded) ucf.SetShowInTaskBar(true);
            }
        }

        public void MakeAllOpaque()
        {
            foreach (UserControlForm ucf in tabforms)
            {
                if (ucf.isloaded)
                {
                    ucf.SetTransparency(false);
                    ucf.SetShowTitleInTransparency(true);
                }
            }
        }

    }
}
