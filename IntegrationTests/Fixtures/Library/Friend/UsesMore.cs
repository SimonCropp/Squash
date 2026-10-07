using Lib;

namespace Friend;

// Internals of Lib that only this assembly uses, in every way the friend roots have to see.
[Lib.Marker(Text = Lib.Constants.Name)]
class UsesMore :
    Lib.Template
{
    internal override string Fill()
    {
        var holder = new Lib.Holder<int>
        {
            Value = 1
        };
        var visible = new Lib.Visible();
        visible.count++;
        return Lib.Constants.Value() +
               Lib.Visible.Hidden +
               visible.Internal() +
               holder.Get() +
               holder.Convert("text") +
               new Lib.Holder<int>.Inner().Name() +
               Lib.Mode.On +
               "text".Doubled() +
               string.Fixed();
    }
}
