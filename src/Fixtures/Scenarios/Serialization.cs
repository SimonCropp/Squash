using System.Runtime.Serialization;

namespace Scenarios;

[Serializable]
public class Serialized :
    ISerializable
{
    public Serialized()
    {
    }

    protected Serialized(SerializationInfo info, StreamingContext context)
    {
    }

    public void GetObjectData(SerializationInfo info, StreamingContext context)
    {
    }

    [OnDeserialized]
    void Deserialized(StreamingContext context)
    {
    }

    void NotACallback()
    {
    }
}

public class SerializedFactory
{
    public object Create() => new SerializedInternal();
}

// Not visible, but referenced: library mode then keeps its serialization constructor and callbacks.
[Serializable]
class SerializedInternal
{
    public SerializedInternal()
    {
    }

    SerializedInternal(SerializationInfo info, StreamingContext context)
    {
    }

    [OnSerializing]
    void Serializing(StreamingContext context)
    {
    }

    void NotACallback()
    {
    }
}
