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
    /// Enum for multi-color direction.
    /// </summary>
    /// <value>Enum for multi-color direction.</value>
    public enum SpoolmanApiV1ModelsMultiColorDirection
    {
        /// <summary>
        /// Enum Coaxial for value: coaxial
        /// </summary>
        Coaxial = 1,

        /// <summary>
        /// Enum Longitudinal for value: longitudinal
        /// </summary>
        Longitudinal = 2
    }

    /// <summary>
    /// Converts <see cref="SpoolmanApiV1ModelsMultiColorDirection"/> to and from the JSON value
    /// </summary>
    public static class SpoolmanApiV1ModelsMultiColorDirectionValueConverter
    {
        /// <summary>
        /// Parses a given value to <see cref="SpoolmanApiV1ModelsMultiColorDirection"/>
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static SpoolmanApiV1ModelsMultiColorDirection FromString(string value)
        {
            if (value.Equals("coaxial"))
                return SpoolmanApiV1ModelsMultiColorDirection.Coaxial;

            if (value.Equals("longitudinal"))
                return SpoolmanApiV1ModelsMultiColorDirection.Longitudinal;

            throw new NotImplementedException($"Could not convert value to type SpoolmanApiV1ModelsMultiColorDirection: '{value}'");
        }

        /// <summary>
        /// Parses a given value to <see cref="SpoolmanApiV1ModelsMultiColorDirection"/>
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static SpoolmanApiV1ModelsMultiColorDirection? FromStringOrDefault(string value)
        {
            if (value.Equals("coaxial"))
                return SpoolmanApiV1ModelsMultiColorDirection.Coaxial;

            if (value.Equals("longitudinal"))
                return SpoolmanApiV1ModelsMultiColorDirection.Longitudinal;

            return null;
        }

        /// <summary>
        /// Converts the <see cref="SpoolmanApiV1ModelsMultiColorDirection"/> to the json value
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public static string ToJsonValue(SpoolmanApiV1ModelsMultiColorDirection value)
        {
            if (value == SpoolmanApiV1ModelsMultiColorDirection.Coaxial)
                return "coaxial";

            if (value == SpoolmanApiV1ModelsMultiColorDirection.Longitudinal)
                return "longitudinal";

            throw new NotImplementedException($"Value could not be handled: '{value}'");
        }
    }

    /// <summary>
    /// A Json converter for type <see cref="SpoolmanApiV1ModelsMultiColorDirection"/>
    /// </summary>
    /// <exception cref="NotImplementedException"></exception>
    public class SpoolmanApiV1ModelsMultiColorDirectionJsonConverter : JsonConverter<SpoolmanApiV1ModelsMultiColorDirection>
    {
        /// <summary>
        /// Returns a  from the Json object
        /// </summary>
        /// <param name="reader"></param>
        /// <param name="typeToConvert"></param>
        /// <param name="options"></param>
        /// <returns></returns>
        public override SpoolmanApiV1ModelsMultiColorDirection Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? rawValue = reader.GetString();

            SpoolmanApiV1ModelsMultiColorDirection? result = rawValue == null
                ? null
                : SpoolmanApiV1ModelsMultiColorDirectionValueConverter.FromStringOrDefault(rawValue);

            if (result != null)
                return result.Value;

            throw new JsonException();
        }

        /// <summary>
        /// Writes the SpoolmanApiV1ModelsMultiColorDirection to the json writer
        /// </summary>
        /// <param name="writer"></param>
        /// <param name="spoolmanApiV1ModelsMultiColorDirection"></param>
        /// <param name="options"></param>
        public override void Write(Utf8JsonWriter writer, SpoolmanApiV1ModelsMultiColorDirection spoolmanApiV1ModelsMultiColorDirection, JsonSerializerOptions options)
        {
            writer.WriteStringValue(spoolmanApiV1ModelsMultiColorDirection.ToString());
        }
    }

    /// <summary>
    /// A Json converter for type <see cref="SpoolmanApiV1ModelsMultiColorDirection"/>
    /// </summary>
    public class SpoolmanApiV1ModelsMultiColorDirectionNullableJsonConverter : JsonConverter<SpoolmanApiV1ModelsMultiColorDirection?>
    {
        /// <summary>
        /// Returns a SpoolmanApiV1ModelsMultiColorDirection from the Json object
        /// </summary>
        /// <param name="reader"></param>
        /// <param name="typeToConvert"></param>
        /// <param name="options"></param>
        /// <returns></returns>
        public override SpoolmanApiV1ModelsMultiColorDirection? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? rawValue = reader.GetString();

            SpoolmanApiV1ModelsMultiColorDirection? result = rawValue == null
                ? null
                : SpoolmanApiV1ModelsMultiColorDirectionValueConverter.FromStringOrDefault(rawValue);

            if (result != null)
                return result.Value;

            throw new JsonException();
        }

        /// <summary>
        /// Writes the DateTime to the json writer
        /// </summary>
        /// <param name="writer"></param>
        /// <param name="spoolmanApiV1ModelsMultiColorDirection"></param>
        /// <param name="options"></param>
        public override void Write(Utf8JsonWriter writer, SpoolmanApiV1ModelsMultiColorDirection? spoolmanApiV1ModelsMultiColorDirection, JsonSerializerOptions options)
        {
            writer.WriteStringValue(spoolmanApiV1ModelsMultiColorDirection?.ToString() ?? "null");
        }
    }
}
