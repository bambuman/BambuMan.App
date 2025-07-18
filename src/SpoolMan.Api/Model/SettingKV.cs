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
    /// SettingKV
    /// </summary>
    public partial class SettingKV : IValidatableObject
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SettingKV" /> class.
        /// </summary>
        /// <param name="key">Setting key.</param>
        /// <param name="setting">Setting value.</param>
        [JsonConstructor]
        public SettingKV(string key, SettingResponse setting)
        {
            Key = key;
            Setting = setting;
            OnCreated();
        }

        partial void OnCreated();

        /// <summary>
        /// Setting key.
        /// </summary>
        /// <value>Setting key.</value>
        [JsonPropertyName("key")]
        public string Key { get; set; }

        /// <summary>
        /// Setting value.
        /// </summary>
        /// <value>Setting value.</value>
        [JsonPropertyName("setting")]
        public SettingResponse Setting { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class SettingKV {\n");
            sb.Append("  Key: ").Append(Key).Append("\n");
            sb.Append("  Setting: ").Append(Setting).Append("\n");
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
    /// A Json converter for type <see cref="SettingKV" />
    /// </summary>
    public class SettingKVJsonConverter : JsonConverter<SettingKV>
    {
        /// <summary>
        /// Deserializes json to <see cref="SettingKV" />
        /// </summary>
        /// <param name="utf8JsonReader"></param>
        /// <param name="typeToConvert"></param>
        /// <param name="jsonSerializerOptions"></param>
        /// <returns></returns>
        /// <exception cref="JsonException"></exception>
        public override SettingKV Read(ref Utf8JsonReader utf8JsonReader, Type typeToConvert, JsonSerializerOptions jsonSerializerOptions)
        {
            int currentDepth = utf8JsonReader.CurrentDepth;

            if (utf8JsonReader.TokenType != JsonTokenType.StartObject && utf8JsonReader.TokenType != JsonTokenType.StartArray)
                throw new JsonException();

            JsonTokenType startingTokenType = utf8JsonReader.TokenType;

            Option<string?> key = default;
            Option<SettingResponse?> setting = default;

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
                        case "key":
                            key = new Option<string?>(utf8JsonReader.GetString()!);
                            break;
                        case "setting":
                            setting = new Option<SettingResponse?>(JsonSerializer.Deserialize<SettingResponse>(ref utf8JsonReader, jsonSerializerOptions)!);
                            break;
                        default:
                            break;
                    }
                }
            }

            if (!key.IsSet)
                throw new ArgumentException("Property is required for class SettingKV.", nameof(key));

            if (!setting.IsSet)
                throw new ArgumentException("Property is required for class SettingKV.", nameof(setting));

            if (key.IsSet && key.Value == null)
                throw new ArgumentNullException(nameof(key), "Property is not nullable for class SettingKV.");

            if (setting.IsSet && setting.Value == null)
                throw new ArgumentNullException(nameof(setting), "Property is not nullable for class SettingKV.");

            return new SettingKV(key.Value!, setting.Value!);
        }

        /// <summary>
        /// Serializes a <see cref="SettingKV" />
        /// </summary>
        /// <param name="writer"></param>
        /// <param name="settingKV"></param>
        /// <param name="jsonSerializerOptions"></param>
        /// <exception cref="NotImplementedException"></exception>
        public override void Write(Utf8JsonWriter writer, SettingKV settingKV, JsonSerializerOptions jsonSerializerOptions)
        {
            writer.WriteStartObject();

            WriteProperties(writer, settingKV, jsonSerializerOptions);
            writer.WriteEndObject();
        }

        /// <summary>
        /// Serializes the properties of <see cref="SettingKV" />
        /// </summary>
        /// <param name="writer"></param>
        /// <param name="settingKV"></param>
        /// <param name="jsonSerializerOptions"></param>
        /// <exception cref="NotImplementedException"></exception>
        public void WriteProperties(Utf8JsonWriter writer, SettingKV settingKV, JsonSerializerOptions jsonSerializerOptions)
        {
            if (settingKV.Key == null)
                throw new ArgumentNullException(nameof(settingKV.Key), "Property is required for class SettingKV.");

            if (settingKV.Setting == null)
                throw new ArgumentNullException(nameof(settingKV.Setting), "Property is required for class SettingKV.");

            writer.WriteString("key", settingKV.Key);

            writer.WritePropertyName("setting");
            JsonSerializer.Serialize(writer, settingKV.Setting, jsonSerializerOptions);
        }
    }
}
