using BattleArena.scripts.interfaces;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace BattleArena.scripts.utils
{
    public class EffectConverter : JsonConverter<IEffect>
    {
        public override IEffect Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeElement))
                throw new JsonException("Missing 'type' field in IEffect JSON.");

            var typeName = typeElement.GetString();
            var parameters = root.GetProperty("params");

            var effectType = EffectRegistry.GetEffectType(typeName);
            if (effectType == null)
                throw new JsonException($"Unknown effect type: {typeName}");

            var effect = Activator.CreateInstance(effectType) as IEffect;
            if (effect is not GodotObject objEffect)
                throw new JsonException($"Effect type {typeName} does not implement GodotObject.");

            foreach (var prop in parameters.EnumerateObject())
            {
                objEffect.Set(prop.Name, JsonValueToVariant(prop.Value));
            }

            return effect;
        }

        public override void Write(Utf8JsonWriter writer, IEffect value, JsonSerializerOptions options)
        {
            if (value is not Resource resource)
                throw new JsonException("IEffect is not a Godot Resource");

            var type = value.GetType();
            string typeName = type.Name;

            writer.WriteStartObject();

            writer.WriteString("type", typeName);

            writer.WritePropertyName("params");
            writer.WriteStartObject();

            foreach (var prop in type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (!prop.CanRead)
                    continue;

                string propName = prop.Name;
                object? propValue = prop.GetValue(value);

                WriteVariantValue(writer, propName, propValue, options);
            }
        }

        private void WriteVariantValue(Utf8JsonWriter writer, string propName, object? value, JsonSerializerOptions options)
        {
            writer.WritePropertyName(propName);

            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }
            try
            {
                // Convert the value to a Variant (Godot Variant can wrap most engine and primitive types)
                Variant variant = Variant.From(value);

                // Use Godot's built-in serializer to produce a JSON string
                string json = Json.Stringify(variant);

                // Parse that string back into a JsonDocument so you can write it with Utf8JsonWriter
                using var doc = JsonDocument.Parse(json);

                // Write the JSON value recursively into your main writer
                doc.RootElement.WriteTo(writer);
            }
            catch (Exception ex)
            {
                GD.PushWarning($"Failed to convert property '{propName}' of type '{value.GetType()}' to Variant. Falling back to string. Exception: {ex.Message}");
                writer.WriteStringValue("");
            }
        }

        private Variant JsonValueToVariant(JsonElement element)
        {
            if (element.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            {
                string rawJson = element.GetRawText();
                var json = new Json();
                var result = json.Parse(rawJson);

                if (result == Error.Ok)
                {
                    return json.Data;
                }
                else
                {
                    GD.PushWarning($"Failed to parse JSON: {result}");
                }
            }
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw new JsonException($"Unsupported JSON kind: {element.ValueKind}")
            };
        }
    }
}
