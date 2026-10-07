using System.ComponentModel;

namespace Scenarios;

// Members a serializer or mapper would reach by reflection, which no IL references. Stock rules
// remove them; SquashPreserve=DataShape keeps them.
public class Shape
{
    Shape()
    {
    }

    Shape(string name) =>
        Name = name;

    public static Shape Create(string name) => new(name);

    public string Name { get; private set; } = "";

    // The initializer writes the backing field, so nothing ever calls this setter.
    public string NeverSet { get; private set; } = "";

    string unusedField = "";
}

// The converter is named only by typeof in an attribute. On .NET the attribute's parameter is
// annotated, so the linker keeps the converter's members. On netstandard2.0 and .NET Framework
// nothing is annotated: the type stays and its constructor goes.
[TypeConverter(typeof(ShapeConverter))]
public class Converted;

class ShapeConverter :
    TypeConverter
{
    public string UnusedProperty { get; set; } = "";
}
