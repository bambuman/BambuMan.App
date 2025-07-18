// <auto-generated>
/*
 * Spoolman REST API v1
 *
 *      REST API for Spoolman.      The API is served on the path `/api/v1/`.      Some endpoints also serve a websocket on the same path. The websocket is used to listen for changes to the data     that the endpoint serves. The websocket messages are JSON objects. Additionally, there is a root-level websocket     endpoint that listens for changes to any data in the database.     
 *
 * The version of the OpenAPI document: 1.0.0
 * Generated by: https://github.com/openapitools/openapi-generator.git
 */

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;
using SpoolMan.Api.Client;

namespace SpoolMan.Api.Model
{
    /// <summary>
    /// SettingResponse
    /// </summary>
    public partial class SettingResponse : IValidatableObject
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SettingResponse" /> class.
        /// </summary>
        /// <param name="value">Setting value.</param>
        /// <param name="isSet">Whether the setting has been set. If false, &#39;value&#39; contains the default value.</param>
        /// <param name="type">Setting type. This corresponds with JSON types.</param>
        [JsonConstructor]
        public SettingResponse(string value, bool isSet, SettingType type)
        {
            Value = value;
            IsSet = isSet;
            Type = type;
            OnCreated();
        }

        partial void OnCreated();

        /// <summary>
        /// Setting type. This corresponds with JSON types.
        /// </summary>
        /// <value>Setting type. This corresponds with JSON types.</value>
        [JsonPropertyName("type")]
        public SettingType Type { get; set; }

        /// <summary>
        /// Setting value.
        /// </summary>
        /// <value>Setting value.</value>
        [JsonPropertyName("value")]
        public string Value { get; set; }

        /// <summary>
        /// Whether the setting has been set. If false, &#39;value&#39; contains the default value.
        /// </summary>
        /// <value>Whether the setting has been set. If false, &#39;value&#39; contains the default value.</value>
        [JsonPropertyName("is_set")]
        public bool IsSet { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class SettingResponse {\n");
            sb.Append("  Value: ").Append(Value).Append("\n");
            sb.Append("  IsSet: ").Append(IsSet).Append("\n");
            sb.Append("  Type: ").Append(Type).Append("\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        /// <summary>
        /// To validate all properties of the instance
        /// </summary>
        /// <param name="validationContext">Validation context</param>
        /// <returns>Validation Result</returns>
        IEnumerable<ValidationResult> IValidatableObject.Validate(ValidationContext validationContext)
        {
            yield break;
        }
    }

    /// <summary>
    /// A Json converter for type <see cref="SettingResponse" />
    /// </summary>
    public class SettingResponseJsonConverter : JsonConverter<SettingResponse>
    {
        /// <summary>
        /// Deserializes json to <see cref="SettingResponse" />
        /// </summary>
        /// <param name="utf8JsonReader"></param>
        /// <param name="typeToConvert"></param>
        /// <param name="jsonSerializerOptions"></param>
        /// <returns></returns>
        /// <exception cref="JsonException"></exception>
        public override SettingResponse Read(ref Utf8JsonReader utf8JsonReader, Type typeToConvert, JsonSerializerOptions jsonSerializerOptions)
        {
            int currentDepth = utf8JsonReader.CurrentDepth;

            if (utf8JsonReader.TokenType != JsonTokenType.StartObject && utf8JsonReader.TokenType != JsonTokenType.StartArray)
                throw new JsonException();

            JsonTokenType startingTokenType = utf8JsonReader.TokenType;

            Option<string?> value = default;
            Option<bool?> isSet = default;
            Option<SettingType?> type = default;

            while (utf8JsonReader.Read())
            {
                if (startingTokenType == JsonTokenType.StartObject && utf8JsonReader.TokenType == JsonTokenType.EndObject && currentDepth == utf8JsonReader.CurrentDepth)
                    break;

                if (startingTokenType == JsonTokenType.StartArray && utf8JsonReader.TokenType == JsonTokenType.EndArray && currentDepth == utf8JsonReader.CurrentDepth)
                    break;

                if (utf8JsonReader.TokenType == JsonTokenType.PropertyName && currentDepth == utf8JsonReader.CurrentDepth - 1)
                {
                    string? localVarJsonPropertyName = utf8JsonReader.GetString();
                    utf8JsonReader.Read();

                    switch (localVarJsonPropertyName)
                    {
                        case "value":
                            value = new Option<string?>(utf8JsonReader.GetString()!);
                            break;
                        case "is_set":
                            isSet = new Option<bool?>(utf8JsonReader.TokenType == JsonTokenType.Null ? (bool?)null : utf8JsonReader.GetBoolean());
                            break;
                        case "type":
                            string? typeRawValue = utf8JsonReader.GetString();
                            if (typeRawValue != null)
                                type = new Option<SettingType?>(SettingTypeValueConverter.FromStringOrDefault(typeRawValue));
                            break;
                        default:
                            break;
                    }
                }
            }

            if (!value.IsSet)
                throw new ArgumentException("Property is required for class SettingResponse.", nameof(value));

            if (!isSet.IsSet)
                throw new ArgumentException("Property is required for class SettingResponse.", nameof(isSet));

            if (!type.IsSet)
                throw new ArgumentException("Property is required for class SettingResponse.", nameof(type));

            if (value.IsSet && value.Value == null)
                throw new ArgumentNullException(nameof(value), "Property is not nullable for class SettingResponse.");

            if (isSet.IsSet && isSet.Value == null)
                throw new ArgumentNullException(nameof(isSet), "Property is not nullable for class SettingResponse.");

            if (type.IsSet && type.Value == null)
                throw new ArgumentNullException(nameof(type), "Property is not nullable for class SettingResponse.");

            return new SettingResponse(value.Value!, isSet.Value!.Value!, type.Value!.Value!);
        }

        /// <summary>
        /// Serializes a <see cref="SettingResponse" />
        /// </summary>
        /// <param name="writer"></param>
        /// <param name="settingResponse"></param>
        /// <param name="jsonSerializerOptions"></param>
        /// <exception cref="NotImplementedException"></exception>
        public override void Write(Utf8JsonWriter writer, SettingResponse settingResponse, JsonSerializerOptions jsonSerializerOptions)
        {
            writer.WriteStartObject();

            WriteProperties(writer, settingResponse, jsonSerializerOptions);
            writer.WriteEndObject();
        }

        /// <summary>
        /// Serializes the properties of <see cref="SettingResponse" />
        /// </summary>
        /// <param name="writer"></param>
        /// <param name="settingResponse"></param>
        /// <param name="jsonSerializerOptions"></param>
        /// <exception cref="NotImplementedException"></exception>
        public void WriteProperties(Utf8JsonWriter writer, SettingResponse settingResponse, JsonSerializerOptions jsonSerializerOptions)
        {
            if (settingResponse.Value == null)
                throw new ArgumentNullException(nameof(settingResponse.Value), "Property is required for class SettingResponse.");

            writer.WriteString("value", settingResponse.Value);

            writer.WriteBoolean("is_set", settingResponse.IsSet);

            var typeRawValue = SettingTypeValueConverter.ToJsonValue(settingResponse.Type);
            writer.WriteString("type", typeRawValue);
        }
    }
}
