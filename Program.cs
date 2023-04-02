namespace basic_wm;

using namespace basic_wm;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;

using TerraFX.Interop.Xlib;
using static TerraFX.Interop.Xlib.Xlib;

using static basic_wm.ConvFunctions;
using static basic_wm.Utils;
using System.Linq;

unsafe static class ConvFunctions
{
    //
    //  Conversion functions for sbyte * and string hacks!
    //
    public static int StringOfSize(string str_value)
    {
        if (str_value == null)
            {
                return 0;
            }
            return (str_value.Length * 4) + 1;
    }

    public static sbyte *StringFromCharPtr(string str_value, sbyte *buf_value, int size_value)
    {
        if (str_value == null)
            {
                return (sbyte*)0;
            }
            fixed (char* strPtr = str_value)
            {
                Encoding.UTF8.GetBytes(strPtr, str_value.Length + 1, (byte *)buf_value, size_value);
            }
            return buf_value;
    }

    public static sbyte *StringFromHeap(string str_value)
    {
        if (str_value == null)
        {
            return (sbyte*)0;
        }

        int strOfSize = StringOfSize(str_value);
        sbyte* buf_value = (sbyte*)Marshal.AllocHGlobal(strOfSize);
        fixed (char* strPtr = str_value)
        {
            Encoding.UTF8.GetBytes(strPtr, str_value.Length + 1, (byte *)buf_value, strOfSize);
        }
        return buf_value;
    }

    public static string CharPtrToString(sbyte *charptr)
    {
        if (charptr == null)
        {
            return string.Empty;
        }

        sbyte *ptr = charptr;
        while (*ptr != 0)
        {
            ptr++;
        }

        return Encoding.UTF8.GetString((byte *)charptr, (int)(ptr - (sbyte *)charptr));
    }
}

unsafe class WindowManager
{
    private static bool wm_detected_;
    private Mutex wm_detected_mutex_;
    private Display *display_;
    private Window root_;
    private Dictionary<Window, Window> clients_;
    private Position<int> drag_start_pos_, drag_start_frame_pos_;
    private Size<uint> drag_start_frame_size_;
    private Atom WM_PROTOCOLS, WM_DELETE_WINDOW;

    public static WindowManager Create(string display_name)
    {
        int display_name_size = StringOfSize(display_name);
        sbyte *display_name_data = stackalloc sbyte[display_name_size];
        Display *display = XOpenDisplay(StringFromCharPtr(display_name, display_name_data, display_name_size));
        if (display == null)
        {
            Console.WriteLine("Error!");
            Environment.Exit(1);
        }
        return new(display);
    }

    private WindowManager(Display *display)
    {
        this.display_ = display;
        this.root_ = XDefaultRootWindow(display_);
        this.WM_PROTOCOLS = XInternAtom(display_, StringFromHeap("WM_PROTOCOLS"), False);
        this.WM_DELETE_WINDOW = XInternAtom(display_, StringFromHeap("WM_DELETE_WINDOW"), False);
        this.clients_ = new Dictionary<Window, Window>();
        this.wm_detected_mutex_ = new Mutex();
    }

    ~WindowManager()
    {
        XCloseDisplay(display_);
    }

    public void Run()
    {
        wm_detected_ = true;
        try
        {
            wm_detected_ = wm_detected_mutex_.WaitOne();

            XSetErrorHandler(&OnWMDetected);
            XSelectInput(display_, root_, SubstructureNotifyMask | SubstructureRedirectMask);
            XSync(display_, False);

            if (wm_detected_)
            {
                Console.WriteLine("Check, if it is an error, {0}", CharPtrToString(XDisplayString(display_)));
            }
        }
        finally
        {
            wm_detected_mutex_.ReleaseMutex();
        }

        XSetErrorHandler(&OnXError);
        XGrabServer(display_);

        Window returned_root, returned_parent;
        Window *top_level_windows;
        uint num_top_level_windows;
        XQueryTree(display_, root_, &returned_root, &returned_parent, &top_level_windows, &num_top_level_windows);

        for (uint i = 0; i < num_top_level_windows; ++i)
        {
            Frame(top_level_windows[i], true);
        }

        XFree(top_level_windows);
        XUngrabServer(display_);

        for (;;)
        {
            XEvent e;
            XNextEvent(display_, &e);

            switch(e.type)
            {
                case CreateNotify:
                    OnCreateNotify(e.xcreatewindow);
                    break;
                case DestroyNotify:
                    OnDestroyNotify(e.xdestroywindow);
                    break;
                case ReparentNotify:
                    OnReparentNotify(e.xreparent);
                    break;
                case MapNotify:
                    OnMapNotify(e.xmap);
                    break;
                case UnmapNotify:
                    OnUnmapNotify(e.xunmap);
                    break;
                case ConfigureNotify:
                    OnConfigureNotify(e.xconfigure);
                    break;
                case MapRequest:
                    OnMapRequest(e.xmaprequest);
                    break;
                case ConfigureRequest:
                    OnConfigureRequest(e.xconfigurerequest);
                    break;
                case ButtonPress:
                    OnButtonPress(e.xbutton);
                    break;
                case ButtonRelease:
                    OnButtonRelease(e.xbutton);
                    break;
                case MotionNotify:
                    while (Convert.ToBoolean(XCheckTypedWindowEvent(display_, e.xmotion.window, MotionNotify, &e)))
                    {
                    }
                    OnMotionNotify(e.xmotion);
                    break;
                case KeyPress:
                    OnKeyPress(e.xkey);
                    break;
                case KeyRelease:
                    OnKeyRelease(e.xkey);
                    break;
            }
        }
    }

    public void Frame(Window w, bool was_created_before_window_manager)
    {
        if (clients_.ContainsKey(w))
        {
            return;
        }

        uint BORDER_WIDTH = 5;
        ulong BORDER_COLOR = 0xff5500;
        ulong BG_COLOR = 0x111111;

        XWindowAttributes x_window_attrs;
        XGetWindowAttributes(display_, w, &x_window_attrs);

        if (was_created_before_window_manager) {
            if (x_window_attrs.override_redirect != False || x_window_attrs.map_state != IsViewable) {
                return;
            }
        }

        Window frame = XCreateSimpleWindow(display_, root_, x_window_attrs.x, x_window_attrs.y, (uint)x_window_attrs.width,
            (uint)x_window_attrs.height, BORDER_WIDTH, (nuint)BORDER_COLOR, (nuint)BG_COLOR);

        XSelectInput(display_, frame, SubstructureRedirectMask | SubstructureNotifyMask);
        XAddToSaveSet(display_, w);
        XReparentWindow(display_, w, frame, 0, 0);
        XMapWindow(display_, frame);
        clients_[w] = frame;

        XGrabButton(display_, Button1, Mod1Mask, w, False, (uint)(ButtonPressMask | ButtonReleaseMask | ButtonMotionMask),
            GrabModeAsync, GrabModeAsync, (Window)None, (Cursor)None);

        XGrabButton(display_, Button3, Mod1Mask, w, False, (uint)(ButtonPressMask | ButtonReleaseMask | ButtonMotionMask),
            GrabModeAsync, GrabModeAsync, (Window)None, (Cursor)None);
        
        XGrabKey(display_, XKeysymToKeycode(display_, (KeySym)XK_F4), Mod1Mask, w, False, GrabModeAsync, GrabModeAsync);

        XGrabKey(display_, XKeysymToKeycode(display_, (KeySym)XK_Tab), Mod1Mask, w, False, GrabModeAsync, GrabModeAsync);
    }

    public void Unframe(Window w)
    {
        if (!clients_.ContainsKey(w))
        {
            return;
        }

        Window frame = clients_[w];
        XUnmapWindow(display_, frame);

        XReparentWindow(display_, w, root_, 0, 0);

        XRemoveFromSaveSet(display_, w);
        XDestroyWindow(display_, frame);

        clients_.Remove(w);
    }

    private void OnCreateNotify(XCreateWindowEvent e)
    {
    }

    private void OnDestroyNotify(XDestroyWindowEvent e)
    {
    }

    private void OnReparentNotify(XReparentEvent e)
    {
    }

    private void OnMapNotify(XMapEvent e)
    {
    }

    private void OnUnmapNotify(XUnmapEvent e)
    {
        if (clients_.ContainsKey(e.window))
        {
            Console.WriteLine("Ignore UnmapNotify for non-client window {0}", e.window);
            return;
        }

        if(e.@event == root_)
        {
            Console.WriteLine("Ignore UnmapNotify for reparented pre-existing window {0}", e.window);
            return;
        }

        Unframe(e.window);
    }

    private void OnConfigureNotify(XConfigureEvent e)
    {
    }

    private void OnMapRequest(XMapRequestEvent e)
    {
        Frame(e.window, false);
        XMapWindow(display_, e.window);
    }

    private void OnConfigureRequest(XConfigureRequestEvent e)
    {
        XWindowChanges changes = new();
        changes.x = e.x;
        changes.y = e.y;
        changes.width = e.width;
        changes.height = e.height;
        changes.border_width = e.border_width;
        changes.sibling = e.above;
        changes.stack_mode = e.detail;

        if (clients_.ContainsKey(e.window))
        {
            Window frame = clients_[e.window];
            XConfigureWindow(display_, frame, (uint)e.value_mask, &changes);
        }

        XConfigureWindow(display_, e.window, (uint)e.value_mask, &changes);
    }

    private void OnButtonPress(XButtonEvent e)
    {
        if(!clients_.ContainsKey(e.window))
        {
            return;
        }

        Window frame = clients_[e.window];
        drag_start_pos_ = new Position<int>(e.x_root, e.y_root);

        Window returned_root;
        int x, y;
        uint width, height, border_width, depth;
        XGetGeometry(display_, (Drawable)frame.Value, &returned_root, &x, &y, &width, &height, &border_width, &depth);

        drag_start_frame_pos_ = new Position<int>(x, y);
        drag_start_frame_size_ = new Size<uint>(width, height);
        XRaiseWindow(display_, frame);
    }

    private void OnButtonRelease(XButtonEvent e)
    {

    }

    private void OnMotionNotify(XMotionEvent e)
    {
        if(!clients_.ContainsKey(e.window))
        {
            return;
        }

        Window frame = clients_[e.window];
        Position<int> drag_pos = new Position<int>(e.x_root, e.y_root);
        Position<int> delta = new();
        delta.X = drag_pos.X - drag_start_pos_.X;
        delta.Y = drag_pos.Y - drag_start_pos_.Y;

        bool state = Convert.ToBoolean(e.state); 
        if (state &= Convert.ToBoolean(Button1Mask))
        {
            Position<int> dest_frame_pos = new();
            dest_frame_pos.X = drag_start_frame_pos_.X + delta.X;
            dest_frame_pos.Y = drag_start_frame_pos_.Y + delta.Y;
            XMoveWindow(display_, frame, dest_frame_pos.X, dest_frame_pos.Y);
        }
        else if (state &= Convert.ToBoolean(Button3Mask))
        {
            Size<uint> size_delta = new();
            size_delta.width = (uint)Math.Max(delta.X, -drag_start_frame_size_.width);
            size_delta.height = (uint)Math.Max(delta.Y, -drag_start_frame_size_.height);

            Size<uint> dest_frame_size = new();
            dest_frame_size.width = drag_start_frame_size_.width + size_delta.width;
            dest_frame_size.height = drag_start_frame_size_.height + size_delta.height;

            XResizeWindow(display_, frame, dest_frame_size.width, dest_frame_size.height);
            XResizeWindow(display_, e.window, dest_frame_size.width, dest_frame_size.height);
        }


    }

    private void OnKeyPress(XKeyEvent e)
    {
        bool state = Convert.ToBoolean(e.state);
        if ((state &= Convert.ToBoolean(Mod1Mask)) && (e.keycode == XKeysymToKeycode(display_, (KeySym)XK_F4)))
        {
            Atom* supported_protocols = stackalloc Atom[1];
            int num_supported_protocols;
            if (!Convert.ToBoolean(XGetWMProtocols(display_, e.window, &supported_protocols, &num_supported_protocols)))
            {
                XEvent msg = new();
                msg.xclient.type = ClientMessage;
                msg.xclient.message_type = WM_PROTOCOLS;
                msg.xclient.window = e.window;
                msg.xclient.format = 32;
                msg.xclient.data.l[0] = WM_DELETE_WINDOW;
                if ( Convert.ToBoolean(XSendEvent(display_, e.window, False, 0, &msg)))
                {
                    return;
                }
            }
            else
            {
                XKillClient(display_, (XID)e.window.Value);
            }
        }
        else if ((state &= Convert.ToBoolean(Mod1Mask)) && (e.keycode == XKeysymToKeycode(display_, (KeySym)XK_Tab)))
        {
            var i = clients_.ContainsKey(e.window);
            if (i == Convert.ToBoolean(clients_.Count()))
            {
                return;
            }

            if (i != Convert.ToBoolean(clients_.Count()))
            {
                i = Convert.ToBoolean(clients_.ElementAt(1));
            }

            XRaiseWindow(display_, e.window);
            XSetInputFocus(display_, e.window, RevertToPointerRoot, (Time)CurrentTime);
        }
    }

    private void OnKeyRelease(XKeyEvent e)
    {

    }

    [UnmanagedCallersOnly]
    private static int OnXError(Display* display, XErrorEvent* e)
    {
        int MAX_ERROR_TEXT_LENGTH = 1024;
        sbyte[] error_text = new sbyte[MAX_ERROR_TEXT_LENGTH];
        fixed (sbyte *error_text_ptrs = error_text)
        {
            XGetErrorText(display, e->error_code, error_text_ptrs, sizeof(sbyte*));
            Console.WriteLine("Received X error:\n");
            Console.WriteLine("\tRequest: {0}", e->request_code);
            Console.WriteLine("\t- {0} \n", XRequestCodeToString(e->request_code));
            Console.WriteLine("\tError code: {0}", e->error_code);
            Console.WriteLine("\t- {0} \n", error_text);
            Console.WriteLine("\tResource ID: {0}", e->resourceid);
        }
        return 0;
    }


    [UnmanagedCallersOnly]
    private static int OnWMDetected(Display* display, XErrorEvent* e)
    {
        if (e->error_code == BadAccess)
        {
            Console.WriteLine("Error bad accesses...");
        }

        wm_detected_ = false;
        return 0;
    }
}

unsafe class Program
{
    static int Main(string[] args)
    {
        WindowManager wm = WindowManager.Create(string.Empty);
        if (wm == null)
        {
            Console.WriteLine("Error!");
            return 1;
        }

        wm.Run();
        Console.WriteLine("Basic WM started.");
        return 0;
    }
};
