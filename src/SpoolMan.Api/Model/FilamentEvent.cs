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
    /// Event.
    /// </summary>
    public partial class FilamentEvent : IValidatableObject
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FilamentEvent" /> class.
        /// </summary>
        /// <param name="type">Event type.</param>
        /// <param name="resource">Resource type.</param>
        /// <param name="date">When the event occured. UTC Timezone.</param>
        /// <param name="payload">Updated filament.</param>
        [JsonConstructor]
        public FilamentEvent(EventType type, ResourceEnum resource, string date, Filament payload)
        {
            Type = type;
            Resource = resource;
            Date = date;
            Payload = payload;
            OnCreated();
        }

        partial void OnCreated();

        /// <summary>
        /// Event type.
        /// </summary>
        /// <value>Event type.</value>
        [JsonPropertyName("type")]
        public EventType Type { get; set; }

        /// <summary>
        /// Resource type.
        /// </summary>
        /// <value>Resource type.</value>
        public enum ResourceEnum
        {
            /// <summary>
            /// Enum Filament for value: filament
            /// </summary>
            Filament = 1
        }

        /// <summary>
        /// Returns a <see cref="ResourceEnum"/>
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public static ResourceEnum ResourceEnumFromString(string value)
        {
            if (value.Equals("filament"))
                return ResourceEnum.Filament;

            throw new NotImplementedException($"Could not convert value to type ResourceEnum: '{value}'");
        }

        /// <summary>
        /// Returns a <see cref="ResourceEnum"/>
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static ResourceEnum? ResourceEnumFromStringOrDefault(string value)
        {
            if (value.Equals("filament"))
                return ResourceEnum.Filament;

            return null;
        }

        /// <summary>
        /// Converts the <see cref="ResourceEnum"/> to the json value
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public static string ResourceEnumToJsonValue(ResourceEnum value)
        {
            if (value == ResourceEnum.Filament)
                return "filament";

            throw new NotImplementedException($"Value could not be handled: '{value}'");
        }

        /// <summary>
        /// Resource type.
        /// </summary>
        /// <value>Resource type.</value>
        [JsonPropertyName("resource")]
        public ResourceEnum Resource { get; set; }

        /// <summary>
        /// When the event occured. UTC Timezone.
        /// </summary>
        /// <value>When the event occured. UTC Timezone.</value>
        [JsonPropertyName("date")]
        public string Date { get; set; }

        /// <summary>
        /// Updated filament.
        /// </summary>
        /// <value>Updated filament.</value>
        [JsonPropertyName("payload")]
        public Filament Payload { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class FilamentEvent {\n");
            sb.Append("  Type: ").Append(Type).Append("\n");
            sb.Append("  Resource: ").Append(Resource).Append("\n");
            sb.Append("  Date: ").Append(Date).Append("\n");
            sb.Append("  Payload: ").Append(Payload).Append("\n");
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
    /// A Json converter for type <see cref="FilamentEvent" />
    /// </summary>
    public class FilamentEventJsonConverter : JsonConverter<FilamentEvent>
    {
        /// <summary>
        /// Deserializes json to <see cref="FilamentEvent" />
        /// </summary>
        /// <param name="utf8JsonReader"></param>
        /// <param name="typeToConvert"></param>
        /// <param name="jsonSerializerOptions"></param>
        /// <returns></returns>
        /// <exception cref="JsonException"></exception>
        public override FilamentEvent Read(ref Utf8JsonReader utf8JsonReader, Type typeToConvert, JsonSerializerOptions jsonSerializerOptions)
        {
            int currentDepth = utf8JsonReader.CurrentDepth;

            if (utf8JsonReader.TokenType != JsonTokenType.StartObject && utf8JsonReader.TokenType != JsonTokenType.StartArray)
                throw new JsonException();

            JsonTokenType startingTokenType = utf8JsonReader.TokenType;

            Option<EventType?> type = default;
            Option<FilamentEvent.ResourceEnum?> resource = default;
            Option<string?> date = default;
            Option<Filament?> payload = default;

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
                        case "type":
                            string? typeRawValue = utf8JsonReader.GetString();
                            if (typeRawValue != null)
                                type = new Option<EventType?>(EventTypeValueConverter.FromStringOrDefault(typeRawValue));
                            break;
                        case "resource":
                            string? resourceRawValue = utf8JsonReader.GetString();
                            if (resourceRawValue != null)
                                resource = new Option<FilamentEvent.ResourceEnum?>(FilamentEvent.ResourceEnumFromStringOrDefault(resourceRawValue));
                            break;
                        case "date":
                            date = new Option<string?>(utf8JsonReader.GetString()!);
                            break;
                        case "payload":
                            payload = new Option<Filament?>(JsonSerializer.Deserialize<Filament>(ref utf8JsonReader, jsonSerializerOptions)!);
                            break;
                        default:
                            break;
                    }
                }
            }

            if (!type.IsSet)
                throw new ArgumentException("Property is required for class FilamentEvent.", nameof(type));

            if (!resource.IsSet)
                throw new ArgumentException("Property is required for class FilamentEvent.", nameof(resource));

            if (!date.IsSet)
                throw new ArgumentException("Property is required for class FilamentEvent.", nameof(date));

            if (!payload.IsSet)
                throw new ArgumentException("Property is required for class FilamentEvent.", nameof(payload));

            if (type.IsSet && type.Value == null)
                throw new ArgumentNullException(nameof(type), "Property is not nullable for class FilamentEvent.");

            if (resource.IsSet && resource.Value == null)
                throw new ArgumentNullException(nameof(resource), "Property is not nullable for class FilamentEvent.");

            if (date.IsSet && date.Value == null)
                throw new ArgumentNullException(nameof(date), "Property is not nullable for class FilamentEvent.");

            if (payload.IsSet && payload.Value == null)
                throw new ArgumentNullException(nameof(payload), "Property is not nullable for class FilamentEvent.");

            return new FilamentEvent(type.Value!.Value!, resource.Value!.Value!, date.Value!, payload.Value!);
        }

        /// <summary>
        /// Serializes a <see cref="FilamentEvent" />
        /// </summary>
        /// <param name="writer"></param>
        /// <param name="filamentEvent"></param>
        /// <param name="jsonSerializerOptions"></param>
        /// <exception cref="NotImplementedException"></exception>
        public override void Write(Utf8JsonWriter writer, FilamentEvent filamentEvent, JsonSerializerOptions jsonSerializerOptions)
        {
            writer.WriteStartObject();

            WriteProperties(writer, filamentEvent, jsonSerializerOptions);
            writer.WriteEndObject();
        }

        /// <summary>
        /// Serializes the properties of <see cref="FilamentEvent" />
        /// </summary>
        /// <param name="writer"></param>
        /// <param name="filamentEvent"></param>
        /// <param name="jsonSerializerOptions"></param>
        /// <exception cref="NotImplementedException"></exception>
        public void WriteProperties(Utf8JsonWriter writer, FilamentEvent filamentEvent, JsonSerializerOptions jsonSerializerOptions)
        {
            if (filamentEvent.Date == null)
                throw new ArgumentNullException(nameof(filamentEvent.Date), "Property is required for class FilamentEvent.");

            if (filamentEvent.Payload == null)
                throw new ArgumentNullException(nameof(filamentEvent.Payload), "Property is required for class FilamentEvent.");

            var typeRawValue = EventTypeValueConverter.ToJsonValue(filamentEvent.Type);
            writer.WriteString("type", typeRawValue);

            var resourceRawValue = FilamentEvent.ResourceEnumToJsonValue(filamentEvent.Resource);
            writer.WriteString("resource", resourceRawValue);
            writer.WriteString("date", filamentEvent.Date);

            writer.WritePropertyName("payload");
            JsonSerializer.Serialize(writer, filamentEvent.Payload, jsonSerializerOptions);
        }
    }
}
