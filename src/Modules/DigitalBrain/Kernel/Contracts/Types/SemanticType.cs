namespace DigitalBrain.Contracts.Types;

public enum PrimitiveKind
{
    String,
    Number,
    Boolean,
    Date,
    DateTime,
    Array,
    Reference,
}

public enum SensitivityClass
{
    Public,
    Personal,
    SpecialCategory,
    Financial,
    Credential,
}

public enum LlmExposure
{
    Value,
    Masked,
    ReferenceOnly,
}

public enum InputWidgetKind
{
    TextBox,
    MultilineTextBox,
    NumberBox,
    DatePicker,
    DateTimePicker,
    Checkbox,
    Select,
    MultiSelect,
    EmailBox,
    UrlBox,
    OutOfBand,
    ReferencePicker,
}

public enum DisplayWidgetKind
{
    Text,
    LongText,
    Number,
    Date,
    DateTime,
    Boolean,
    Choice,
    MultiChoice,
    Email,
    Link,
    Masked,
    Reference,
}

public enum RedactorKind
{
    None,
    Mask,
    Erase,
    Hmac,
}

[GenerateSerializer, Alias("semantic.type")]
public sealed record SemanticType(
    [property: Id(0)] FieldKind Kind,
    [property: Id(1)] string Id,
    [property: Id(2)] PrimitiveKind Primitive,
    [property: Id(3)] SensitivityClass Sensitivity,
    [property: Id(4)] LlmExposure LlmExposure,
    [property: Id(5)] InputWidgetKind InputWidget,
    [property: Id(6)] DisplayWidgetKind DisplayWidget,
    [property: Id(7)] RedactorKind Redactor,
    [property: Id(8)] bool Indexable);