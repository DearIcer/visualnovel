using Godot;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace 交互式文本.Data
{
    /// <summary>
    /// System.Text.Json 转换器：支持 Godot.Variant 的常用类型（int/float/bool/string）。
    /// </summary>
    public class VariantConverter : JsonConverter<Variant>
    {
        public override Variant Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.True: return true;
                case JsonTokenType.False: return false;
                case JsonTokenType.Number:
                    if (reader.TryGetInt32(out int intVal))
                        return intVal;
                    if (reader.TryGetSingle(out float floatVal))
                        return floatVal;
                    return reader.GetDouble();
                case JsonTokenType.String: return reader.GetString();
                default: return default;
            }
        }

        public override void Write(Utf8JsonWriter writer, Variant value, JsonSerializerOptions options)
        {
            if (value.VariantType == Variant.Type.Bool)
                writer.WriteBooleanValue(value.AsBool());
            else if (value.VariantType == Variant.Type.Int)
                writer.WriteNumberValue(value.AsInt64());
            else if (value.VariantType == Variant.Type.Float)
                writer.WriteNumberValue(value.AsDouble());
            else if (value.VariantType == Variant.Type.String)
                writer.WriteStringValue(value.AsString());
            else
                writer.WriteNullValue();
        }
    }
}
