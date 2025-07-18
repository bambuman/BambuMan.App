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
    /// SpoolUpdateParameters
    /// </summary>
    public partial class SpoolUpdateParameters : IValidatableObject
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SpoolUpdateParameters" /> class.
        /// </summary>
        /// <param name="firstUsed">firstUsed</param>
        /// <param name="lastUsed">lastUsed</param>
        /// <param name="filamentId">filamentId</param>
        /// <param name="price">price</param>
        /// <param name="initialWeight">initialWeight</param>
        /// <param name="spoolWeight">spoolWeight</param>
        /// <param name="remainingWeight">remainingWeight</param>
        /// <param name="usedWeight">usedWeight</param>
        /// <param name="location">location</param>
        /// <param name="lotNr">lotNr</param>
        /// <param name="comment">comment</param>
        /// <param name="archived">Whether this spool is archived and should not be used anymore. (default to false)</param>
        /// <param name="extra">extra</param>
        [JsonConstructor]
        public SpoolUpdateParameters(Option<DateTime?> firstUsed = default, Option<DateTime?> lastUsed = default, Option<int?> filamentId = default, Option<decimal?> price = default, Option<decimal?> initialWeight = default, Option<decimal?> spoolWeight = default, Option<decimal?> remainingWeight = default, Option<decimal?> usedWeight = default, Option<string?> location = default, Option<string?> lotNr = default, Option<string?> comment = default, Option<bool?> archived = default, Option<Dictionary<string, string>?> extra = default)
        {
            FirstUsedOption = firstUsed;
            LastUsedOption = lastUsed;
            FilamentIdOption = filamentId;
            PriceOption = price;
            InitialWeightOption = initialWeight;
            SpoolWeightOption = spoolWeight;
            RemainingWeightOption = remainingWeight;
            UsedWeightOption = usedWeight;
            LocationOption = location;
            LotNrOption = lotNr;
            CommentOption = comment;
            ArchivedOption = archived;
            ExtraOption = extra;
            OnCreated();
        }

        partial void OnCreated();

        /// <summary>
        /// Used to track the state of FirstUsed
        /// </summary>
        [JsonIgnore]
        [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
        public Option<DateTime?> FirstUsedOption { get; private set; }

        /// <summary>
        /// Gets or Sets FirstUsed
        /// </summary>
        [JsonPropertyName("first_used")]
        public DateTime? FirstUsed { get { return this.FirstUsedOption; } set { this.FirstUsedOption = new(value); } }

        /// <summary>
        /// Used to track the state of LastUsed
        /// </summary>
        [JsonIgnore]
        [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
        public Option<DateTime?> LastUsedOption { get; private set; }

        /// <summary>
        /// Gets or Sets LastUsed
        /// </summary>
        [JsonPropertyName("last_used")]
        public DateTime? LastUsed { get { return this.LastUsedOption; } set { this.LastUsedOption = new(value); } }

        /// <summary>
        /// Used to track the state of FilamentId
        /// </summary>
        [JsonIgnore]
        [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
        public Option<int?> FilamentIdOption { get; private set; }

        /// <summary>
        /// Gets or Sets FilamentId
        /// </summary>
        [JsonPropertyName("filament_id")]
        public int? FilamentId { get { return this.FilamentIdOption; } set { this.FilamentIdOption = new(value); } }

        /// <summary>
        /// Used to track the state of Price
        /// </summary>
        [JsonIgnore]
        [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
        public Option<decimal?> PriceOption { get; private set; }

        /// <summary>
        /// Gets or Sets Price
        /// </summary>
        [JsonPropertyName("price")]
        public decimal? Price { get { return this.PriceOption; } set { this.PriceOption = new(value); } }

        /// <summary>
        /// Used to track the state of InitialWeight
        /// </summary>
        [JsonIgnore]
        [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
        public Option<decimal?> InitialWeightOption { get; private set; }

        /// <summary>
        /// Gets or Sets InitialWeight
        /// </summary>
        [JsonPropertyName("initial_weight")]
        public decimal? InitialWeight { get { return this.InitialWeightOption; } set { this.InitialWeightOption = new(value); } }

        /// <summary>
        /// Used to track the state of SpoolWeight
        /// </summary>
        [JsonIgnore]
        [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
        public Option<decimal?> SpoolWeightOption { get; private set; }

        /// <summary>
        /// Gets or Sets SpoolWeight
        /// </summary>
        [JsonPropertyName("spool_weight")]
        public decimal? SpoolWeight { get { return this.SpoolWeightOption; } set { this.SpoolWeightOption = new(value); } }

        /// <summary>
        /// Used to track the state of RemainingWeight
        /// </summary>
        [JsonIgnore]
        [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
        public Option<decimal?> RemainingWeightOption { get; private set; }

        /// <summary>
        /// Gets or Sets RemainingWeight
        /// </summary>
        [JsonPropertyName("remaining_weight")]
        public decimal? RemainingWeight { get { return this.RemainingWeightOption; } set { this.RemainingWeightOption = new(value); } }

        /// <summary>
        /// Used to track the state of UsedWeight
        /// </summary>
        [JsonIgnore]
        [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
        public Option<decimal?> UsedWeightOption { get; private set; }

        /// <summary>
        /// Gets or Sets UsedWeight
        /// </summary>
        [JsonPropertyName("used_weight")]
        public decimal? UsedWeight { get { return this.UsedWeightOption; } set { this.UsedWeightOption = new(value); } }

        /// <summary>
        /// Used to track the state of Location
        /// </summary>
        [JsonIgnore]
        [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
        public Option<string?> LocationOption { get; private set; }

        /// <summary>
        /// Gets or Sets Location
        /// </summary>
        [JsonPropertyName("location")]
        public string? Location { get { return this.LocationOption; } set { this.LocationOption = new(value); } }

        /// <summary>
        /// Used to track the state of LotNr
        /// </summary>
        [JsonIgnore]
        [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
        public Option<string?> LotNrOption { get; private set; }

        /// <summary>
        /// Gets or Sets LotNr
        /// </summary>
        [JsonPropertyName("lot_nr")]
        public string? LotNr { get { return this.LotNrOption; } set { this.LotNrOption = new(value); } }

        /// <summary>
        /// Used to track the state of Comment
        /// </summary>
        [JsonIgnore]
        [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
        public Option<string?> CommentOption { get; private set; }

        /// <summary>
        /// Gets or Sets Comment
        /// </summary>
        [JsonPropertyName("comment")]
        public string? Comment { get { return this.CommentOption; } set { this.CommentOption = new(value); } }

        /// <summary>
        /// Used to track the state of Archived
        /// </summary>
        [JsonIgnore]
        [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
        public Option<bool?> ArchivedOption { get; private set; }

        /// <summary>
        /// Whether this spool is archived and should not be used anymore.
        /// </summary>
        /// <value>Whether this spool is archived and should not be used anymore.</value>
        [JsonPropertyName("archived")]
        public bool? Archived { get { return this.ArchivedOption; } set { this.ArchivedOption = new(value); } }

        /// <summary>
        /// Used to track the state of Extra
        /// </summary>
        [JsonIgnore]
        [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
        public Option<Dictionary<string, string>?> ExtraOption { get; private set; }

        /// <summary>
        /// Gets or Sets Extra
        /// </summary>
        [JsonPropertyName("extra")]
        public Dictionary<string, string>? Extra { get { return this.ExtraOption; } set { this.ExtraOption = new(value); } }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class SpoolUpdateParameters {\n");
            sb.Append("  FirstUsed: ").Append(FirstUsed).Append("\n");
            sb.Append("  LastUsed: ").Append(LastUsed).Append("\n");
            sb.Append("  FilamentId: ").Append(FilamentId).Append("\n");
            sb.Append("  Price: ").Append(Price).Append("\n");
            sb.Append("  InitialWeight: ").Append(InitialWeight).Append("\n");
            sb.Append("  SpoolWeight: ").Append(SpoolWeight).Append("\n");
            sb.Append("  RemainingWeight: ").Append(RemainingWeight).Append("\n");
            sb.Append("  UsedWeight: ").Append(UsedWeight).Append("\n");
            sb.Append("  Location: ").Append(Location).Append("\n");
            sb.Append("  LotNr: ").Append(LotNr).Append("\n");
            sb.Append("  Comment: ").Append(Comment).Append("\n");
            sb.Append("  Archived: ").Append(Archived).Append("\n");
            sb.Append("  Extra: ").Append(Extra).Append("\n");
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
            // Price (decimal) minimum
            if (this.PriceOption.IsSet && this.PriceOption.Value < (decimal)0)
            {
                yield return new ValidationResult("Invalid value for Price, must be a value greater than or equal to 0.", new [] { "Price" });
            }

            // InitialWeight (decimal) minimum
            if (this.InitialWeightOption.IsSet && this.InitialWeightOption.Value < (decimal)0)
            {
                yield return new ValidationResult("Invalid value for InitialWeight, must be a value greater than or equal to 0.", new [] { "InitialWeight" });
            }

            // SpoolWeight (decimal) minimum
            if (this.SpoolWeightOption.IsSet && this.SpoolWeightOption.Value < (decimal)0)
            {
                yield return new ValidationResult("Invalid value for SpoolWeight, must be a value greater than or equal to 0.", new [] { "SpoolWeight" });
            }

            // RemainingWeight (decimal) minimum
            if (this.RemainingWeightOption.IsSet && this.RemainingWeightOption.Value < (decimal)0)
            {
                yield return new ValidationResult("Invalid value for RemainingWeight, must be a value greater than or equal to 0.", new [] { "RemainingWeight" });
            }

            // UsedWeight (decimal) minimum
            if (this.UsedWeightOption.IsSet && this.UsedWeightOption.Value < (decimal)0)
            {
                yield return new ValidationResult("Invalid value for UsedWeight, must be a value greater than or equal to 0.", new [] { "UsedWeight" });
            }

            // Location (string) maxLength
            if (this.Location != null && this.Location.Length > 64)
            {
                yield return new ValidationResult("Invalid value for Location, length must be less than 64.", new [] { "Location" });
            }

            // LotNr (string) maxLength
            if (this.LotNr != null && this.LotNr.Length > 64)
            {
                yield return new ValidationResult("Invalid value for LotNr, length must be less than 64.", new [] { "LotNr" });
            }

            // Comment (string) maxLength
            if (this.Comment != null && this.Comment.Length > 1024)
            {
                yield return new ValidationResult("Invalid value for Comment, length must be less than 1024.", new [] { "Comment" });
            }

            yield break;
        }
    }

    /// <summary>
    /// A Json converter for type <see cref="SpoolUpdateParameters" />
    /// </summary>
    public class SpoolUpdateParametersJsonConverter : JsonConverter<SpoolUpdateParameters>
    {
        /// <summary>
        /// The format to use to serialize FirstUsed
        /// </summary>
        public static string FirstUsedFormat { get; set; } = "yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fffffffK";

        /// <summary>
        /// The format to use to serialize LastUsed
        /// </summary>
        public static string LastUsedFormat { get; set; } = "yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fffffffK";

        /// <summary>
        /// Deserializes json to <see cref="SpoolUpdateParameters" />
        /// </summary>
        /// <param name="utf8JsonReader"></param>
        /// <param name="typeToConvert"></param>
        /// <param name="jsonSerializerOptions"></param>
        /// <returns></returns>
        /// <exception cref="JsonException"></exception>
        public override SpoolUpdateParameters Read(ref Utf8JsonReader utf8JsonReader, Type typeToConvert, JsonSerializerOptions jsonSerializerOptions)
        {
            int currentDepth = utf8JsonReader.CurrentDepth;

            if (utf8JsonReader.TokenType != JsonTokenType.StartObject && utf8JsonReader.TokenType != JsonTokenType.StartArray)
                throw new JsonException();

            JsonTokenType startingTokenType = utf8JsonReader.TokenType;

            Option<DateTime?> firstUsed = default;
            Option<DateTime?> lastUsed = default;
            Option<int?> filamentId = default;
            Option<decimal?> price = default;
            Option<decimal?> initialWeight = default;
            Option<decimal?> spoolWeight = default;
            Option<decimal?> remainingWeight = default;
            Option<decimal?> usedWeight = default;
            Option<string?> location = default;
            Option<string?> lotNr = default;
            Option<string?> comment = default;
            Option<bool?> archived = default;
            Option<Dictionary<string, string>?> extra = default;

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
                        case "first_used":
                            firstUsed = new Option<DateTime?>(JsonSerializer.Deserialize<DateTime?>(ref utf8JsonReader, jsonSerializerOptions));
                            break;
                        case "last_used":
                            lastUsed = new Option<DateTime?>(JsonSerializer.Deserialize<DateTime?>(ref utf8JsonReader, jsonSerializerOptions));
                            break;
                        case "filament_id":
                            filamentId = new Option<int?>(utf8JsonReader.TokenType == JsonTokenType.Null ? (int?)null : utf8JsonReader.GetInt32());
                            break;
                        case "price":
                            price = new Option<decimal?>(utf8JsonReader.TokenType == JsonTokenType.Null ? (decimal?)null : utf8JsonReader.GetDecimal());
                            break;
                        case "initial_weight":
                            initialWeight = new Option<decimal?>(utf8JsonReader.TokenType == JsonTokenType.Null ? (decimal?)null : utf8JsonReader.GetDecimal());
                            break;
                        case "spool_weight":
                            spoolWeight = new Option<decimal?>(utf8JsonReader.TokenType == JsonTokenType.Null ? (decimal?)null : utf8JsonReader.GetDecimal());
                            break;
                        case "remaining_weight":
                            remainingWeight = new Option<decimal?>(utf8JsonReader.TokenType == JsonTokenType.Null ? (decimal?)null : utf8JsonReader.GetDecimal());
                            break;
                        case "used_weight":
                            usedWeight = new Option<decimal?>(utf8JsonReader.TokenType == JsonTokenType.Null ? (decimal?)null : utf8JsonReader.GetDecimal());
                            break;
                        case "location":
                            location = new Option<string?>(utf8JsonReader.GetString());
                            break;
                        case "lot_nr":
                            lotNr = new Option<string?>(utf8JsonReader.GetString());
                            break;
                        case "comment":
                            comment = new Option<string?>(utf8JsonReader.GetString());
                            break;
                        case "archived":
                            archived = new Option<bool?>(utf8JsonReader.TokenType == JsonTokenType.Null ? (bool?)null : utf8JsonReader.GetBoolean());
                            break;
                        case "extra":
                            extra = new Option<Dictionary<string, string>?>(JsonSerializer.Deserialize<Dictionary<string, string>>(ref utf8JsonReader, jsonSerializerOptions));
                            break;
                        default:
                            break;
                    }
                }
            }

            if (archived.IsSet && archived.Value == null)
                throw new ArgumentNullException(nameof(archived), "Property is not nullable for class SpoolUpdateParameters.");

            return new SpoolUpdateParameters(firstUsed, lastUsed, filamentId, price, initialWeight, spoolWeight, remainingWeight, usedWeight, location, lotNr, comment, archived, extra);
        }

        /// <summary>
        /// Serializes a <see cref="SpoolUpdateParameters" />
        /// </summary>
        /// <param name="writer"></param>
        /// <param name="spoolUpdateParameters"></param>
        /// <param name="jsonSerializerOptions"></param>
        /// <exception cref="NotImplementedException"></exception>
        public override void Write(Utf8JsonWriter writer, SpoolUpdateParameters spoolUpdateParameters, JsonSerializerOptions jsonSerializerOptions)
        {
            writer.WriteStartObject();

            WriteProperties(writer, spoolUpdateParameters, jsonSerializerOptions);
            writer.WriteEndObject();
        }

        /// <summary>
        /// Serializes the properties of <see cref="SpoolUpdateParameters" />
        /// </summary>
        /// <param name="writer"></param>
        /// <param name="spoolUpdateParameters"></param>
        /// <param name="jsonSerializerOptions"></param>
        /// <exception cref="NotImplementedException"></exception>
        public void WriteProperties(Utf8JsonWriter writer, SpoolUpdateParameters spoolUpdateParameters, JsonSerializerOptions jsonSerializerOptions)
        {
            if (spoolUpdateParameters.FirstUsedOption.IsSet)
                if (spoolUpdateParameters.FirstUsedOption.Value != null)
                    writer.WriteString("first_used", spoolUpdateParameters.FirstUsedOption.Value!.Value.ToString(FirstUsedFormat));
                else
                    writer.WriteNull("first_used");

            if (spoolUpdateParameters.LastUsedOption.IsSet)
                if (spoolUpdateParameters.LastUsedOption.Value != null)
                    writer.WriteString("last_used", spoolUpdateParameters.LastUsedOption.Value!.Value.ToString(LastUsedFormat));
                else
                    writer.WriteNull("last_used");

            if (spoolUpdateParameters.FilamentIdOption.IsSet)
                if (spoolUpdateParameters.FilamentIdOption.Value != null)
                    writer.WriteNumber("filament_id", spoolUpdateParameters.FilamentIdOption.Value!.Value);
                else
                    writer.WriteNull("filament_id");

            if (spoolUpdateParameters.PriceOption.IsSet)
                if (spoolUpdateParameters.PriceOption.Value != null)
                    writer.WriteNumber("price", spoolUpdateParameters.PriceOption.Value!.Value);
                else
                    writer.WriteNull("price");

            if (spoolUpdateParameters.InitialWeightOption.IsSet)
                if (spoolUpdateParameters.InitialWeightOption.Value != null)
                    writer.WriteNumber("initial_weight", spoolUpdateParameters.InitialWeightOption.Value!.Value);
                else
                    writer.WriteNull("initial_weight");

            if (spoolUpdateParameters.SpoolWeightOption.IsSet)
                if (spoolUpdateParameters.SpoolWeightOption.Value != null)
                    writer.WriteNumber("spool_weight", spoolUpdateParameters.SpoolWeightOption.Value!.Value);
                else
                    writer.WriteNull("spool_weight");

            if (spoolUpdateParameters.RemainingWeightOption.IsSet)
                if (spoolUpdateParameters.RemainingWeightOption.Value != null)
                    writer.WriteNumber("remaining_weight", spoolUpdateParameters.RemainingWeightOption.Value!.Value);
                else
                    writer.WriteNull("remaining_weight");

            if (spoolUpdateParameters.UsedWeightOption.IsSet)
                if (spoolUpdateParameters.UsedWeightOption.Value != null)
                    writer.WriteNumber("used_weight", spoolUpdateParameters.UsedWeightOption.Value!.Value);
                else
                    writer.WriteNull("used_weight");

            if (spoolUpdateParameters.LocationOption.IsSet)
                if (spoolUpdateParameters.LocationOption.Value != null)
                    writer.WriteString("location", spoolUpdateParameters.Location);
                else
                    writer.WriteNull("location");

            if (spoolUpdateParameters.LotNrOption.IsSet)
                if (spoolUpdateParameters.LotNrOption.Value != null)
                    writer.WriteString("lot_nr", spoolUpdateParameters.LotNr);
                else
                    writer.WriteNull("lot_nr");

            if (spoolUpdateParameters.CommentOption.IsSet)
                if (spoolUpdateParameters.CommentOption.Value != null)
                    writer.WriteString("comment", spoolUpdateParameters.Comment);
                else
                    writer.WriteNull("comment");

            if (spoolUpdateParameters.ArchivedOption.IsSet)
                writer.WriteBoolean("archived", spoolUpdateParameters.ArchivedOption.Value!.Value);

            if (spoolUpdateParameters.ExtraOption.IsSet)
                if (spoolUpdateParameters.ExtraOption.Value != null)
                {
                    writer.WritePropertyName("extra");
                    JsonSerializer.Serialize(writer, spoolUpdateParameters.Extra, jsonSerializerOptions);
                }
                else
                    writer.WriteNull("extra");
        }
    }
}
