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
    /// ValidationErrorLocInner
    /// </summary>
    public partial class ValidationErrorLocInner : IValidatableObject
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ValidationErrorLocInner" /> class.
        /// </summary>
        /// <param name="string"></param>
        /// <param name="int"></param>
        internal ValidationErrorLocInner(Option<string?> @string, Option<int?> @int)
        {
            StringOption = @string;
            IntOption = @int;
            OnCreated();
        }

        partial void OnCreated();

        /// <summary>
        /// Used to track the state of String
        /// </summary>
        [JsonIgnore]
        [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
        public Option<string?> StringOption { get; private set; }

        /// <summary>
        /// Gets or Sets String
        /// </summary>
        public string? String { get { return this.StringOption; } set { this.StringOption = new(value); } }

        /// <summary>
        /// Used to track the state of Int
        /// </summary>
        [JsonIgnore]
        [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
        public Option<int?> IntOption { get; private set; }

        /// <summary>
        /// Gets or Sets Int
        /// </summary>
        public int? Int { get { return this.IntOption; } set { this.IntOption = new(value); } }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class ValidationErrorLocInner {\n");
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
    /// A Json converter for type <see cref="ValidationErrorLocInner" />
    /// </summary>
    public class ValidationErrorLocInnerJsonConverter : JsonConverter<ValidationErrorLocInner>
    {
        /// <summary>
        /// Deserializes json to <see cref="ValidationErrorLocInner" />
        /// </summary>
        /// <param name="utf8JsonReader"></param>
        /// <param name="typeToConvert"></param>
        /// <param name="jsonSerializerOptions"></param>
        /// <returns></returns>
        /// <exception cref="JsonException"></exception>
        public override ValidationErrorLocInner Read(ref Utf8JsonReader utf8JsonReader, Type typeToConvert, JsonSerializerOptions jsonSerializerOptions)
        {
            int currentDepth = utf8JsonReader.CurrentDepth;

            if (utf8JsonReader.TokenType != JsonTokenType.StartObject && utf8JsonReader.TokenType != JsonTokenType.StartArray)
                throw new JsonException();

            JsonTokenType startingTokenType = utf8JsonReader.TokenType;

            string? varString = default;
            int? varInt = default;

            Utf8JsonReader utf8JsonReaderAnyOf = utf8JsonReader;
            while (utf8JsonReaderAnyOf.Read())
            {
                if (startingTokenType == JsonTokenType.StartObject && utf8JsonReaderAnyOf.TokenType == JsonTokenType.EndObject && currentDepth == utf8JsonReaderAnyOf.CurrentDepth)
                    break;

                if (startingTokenType == JsonTokenType.StartArray && utf8JsonReaderAnyOf.TokenType == JsonTokenType.EndArray && currentDepth == utf8JsonReaderAnyOf.CurrentDepth)
                    break;

                if (utf8JsonReaderAnyOf.TokenType == JsonTokenType.PropertyName && currentDepth == utf8JsonReaderAnyOf.CurrentDepth - 1)
                {
                    Utf8JsonReader utf8JsonReaderString = utf8JsonReader;
                    ClientUtils.TryDeserialize<string?>(ref utf8JsonReaderString, jsonSerializerOptions, out varString);

                    Utf8JsonReader utf8JsonReaderInt = utf8JsonReader;
                    ClientUtils.TryDeserialize<int?>(ref utf8JsonReaderInt, jsonSerializerOptions, out varInt);
                }
            }

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
                        default:
                            break;
                    }
                }
            }

            Option<string?> varStringParsedValue = varString == null
                ? default
                : new Option<string?>(varString);
            Option<int?> varIntParsedValue = varInt == null
                ? default
                : new Option<int?>(varInt);

            return new ValidationErrorLocInner(varStringParsedValue, varIntParsedValue);
        }

        /// <summary>
        /// Serializes a <see cref="ValidationErrorLocInner" />
        /// </summary>
        /// <param name="writer"></param>
        /// <param name="validationErrorLocInner"></param>
        /// <param name="jsonSerializerOptions"></param>
        /// <exception cref="NotImplementedException"></exception>
        public override void Write(Utf8JsonWriter writer, ValidationErrorLocInner validationErrorLocInner, JsonSerializerOptions jsonSerializerOptions)
        {
            writer.WriteStartObject();

            if (validationErrorLocInner.StringOption.IsSet && validationErrorLocInner.StringOption.Value != null)
                writer.WriteString("inner", validationErrorLocInner.StringOption.Value);

            if (validationErrorLocInner.IntOption.IsSet && validationErrorLocInner.IntOption.Value != null)
                writer.WriteNumber("inner", validationErrorLocInner.IntOption.Value.Value);

            WriteProperties(writer, validationErrorLocInner, jsonSerializerOptions);
            writer.WriteEndObject();
        }

        /// <summary>
        /// Serializes the properties of <see cref="ValidationErrorLocInner" />
        /// </summary>
        /// <param name="writer"></param>
        /// <param name="validationErrorLocInner"></param>
        /// <param name="jsonSerializerOptions"></param>
        /// <exception cref="NotImplementedException"></exception>
        public void WriteProperties(Utf8JsonWriter writer, ValidationErrorLocInner validationErrorLocInner, JsonSerializerOptions jsonSerializerOptions)
        {

        }
    }
}
