# basic-wm
WARNING FOR C# not C++

I port from C++ [basic_wm](https://github.com/jichu4n/basic_wm) to C# with package [TerraFX.Interop.Xlib](https://github.com/terrafx/terrafx.interop.xlib)

![image](https://user-images.githubusercontent.com/57066679/229118874-0fce16b0-b089-45af-85d7-56b47a3e52f1.png)

But I need find to resolve my problem because client_.count(w), .erase(w) and std::find() I don't know how do I port to C#

I hope you know that.

PS: Fixed with Dictionary - & in if-else statements

But it seems not working :(

Copy xinitrc from basic_wm of C++/C to native aot or release version basic_wm execuatble.

`XEPHYR=$(whereis -b Xephyr | cut -f2 -d' ')`

`xinit ./xinitrc -- "$XEPHYR" :100 -ac -screen 800x600 -host-cursor`

Then It seems no window border :( How do I fix?
