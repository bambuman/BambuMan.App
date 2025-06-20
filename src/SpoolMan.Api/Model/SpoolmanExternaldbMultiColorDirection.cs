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
    /// Defines spoolman__externaldb__MultiColorDirection
    /// </summary>
    public enum SpoolmanExternaldbMultiColorDirection : int
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
    /// Converts <see cref="SpoolmanExternaldbMultiColorDirection"/> to and from the JSON value
    /// </summary>
    public static class SpoolmanExternaldbMultiColorDirectionValueConverter
    {
        /// <summary>
        /// Parses a given value to <see cref="SpoolmanExternaldbMultiColorDirection"/>
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static SpoolmanExternaldbMultiColorDirection FromString(string value)
        {
            if (value.Equals("coaxial"))
                return SpoolmanExternaldbMultiColorDirection.Coaxial;

            if (value.Equals("longitudinal"))
                return SpoolmanExternaldbMultiColorDirection.Longitudinal;

            throw new NotImplementedException($"Could not convert value to type SpoolmanExternaldbMultiColorDirection: '{value}'");
        }

        /// <summary>
        /// Parses a given value to <see cref="SpoolmanExternaldbMultiColorDirection"/>
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static SpoolmanExternaldbMultiColorDirection? FromStringOrDefault(string value)
        {
            if (value.Equals("coaxial"))
                return SpoolmanExternaldbMultiColorDirection.Coaxial;

            if (value.Equals("longitudinal"))
                return SpoolmanExternaldbMultiColorDirection.Longitudinal;

            return null;
        }

        /// <summary>
        /// Converts the <see cref="SpoolmanExternaldbMultiColorDirection"/> to the json value
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public static string ToJsonValue(SpoolmanExternaldbMultiColorDirection value)
        {
            if (value == SpoolmanExternaldbMultiColorDirection.Coaxial)
                return "coaxial";

            if (value == SpoolmanExternaldbMultiColorDirection.Longitudinal)
                return "longitudinal";

            throw new NotImplementedException($"Value could not be handled: '{value}'");
        }
    }

    /// <summary>
    /// A Json converter for type <see cref="SpoolmanExternaldbMultiColorDirection"/>
    /// </summary>
    /// <exception cref="NotImplementedException"></exception>
    public class SpoolmanExternaldbMultiColorDirectionJsonConverter : JsonConverter<SpoolmanExternaldbMultiColorDirection>
    {
        /// <summary>
        /// Returns a  from the Json object
        /// </summary>
        /// <param name="reader"></param>
        /// <param name="typeToConvert"></param>
        /// <param name="options"></param>
        /// <returns></returns>
        public override SpoolmanExternaldbMultiColorDirection Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? rawValue = reader.GetString();

            SpoolmanExternaldbMultiColorDirection? result = rawValue == null
                ? null
                : SpoolmanExternaldbMultiColorDirectionValueConverter.FromStringOrDefault(rawValue);

            if (result != null)
                return result.Value;

            throw new JsonException();
        }

        /// <summary>
        /// Writes the SpoolmanExternaldbMultiColorDirection to the json writer
        /// </summary>
        /// <param name="writer"></param>
        /// <param name="spoolmanExternaldbMultiColorDirection"></param>
        /// <param name="options"></param>
        public override void Write(Utf8JsonWriter writer, SpoolmanExternaldbMultiColorDirection spoolmanExternaldbMultiColorDirection, JsonSerializerOptions options)
        {
            writer.WriteStringValue(spoolmanExternaldbMultiColorDirection.ToString());
        }
    }

    /// <summary>
    /// A Json converter for type <see cref="SpoolmanExternaldbMultiColorDirection"/>
    /// </summary>
    public class SpoolmanExternaldbMultiColorDirectionNullableJsonConverter : JsonConverter<SpoolmanExternaldbMultiColorDirection?>
    {
        /// <summary>
        /// Returns a SpoolmanExternaldbMultiColorDirection from the Json object
        /// </summary>
        /// <param name="reader"></param>
        /// <param name="typeToConvert"></param>
        /// <param name="options"></param>
        /// <returns></returns>
        public override SpoolmanExternaldbMultiColorDirection? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? rawValue = reader.GetString();

            SpoolmanExternaldbMultiColorDirection? result = rawValue == null
                ? null
                : SpoolmanExternaldbMultiColorDirectionValueConverter.FromStringOrDefault(rawValue);

            if (result != null)
                return result.Value;

            throw new JsonException();
        }

        /// <summary>
        /// Writes the DateTime to the json writer
        /// </summary>
        /// <param name="writer"></param>
        /// <param name="spoolmanExternaldbMultiColorDirection"></param>
        /// <param name="options"></param>
        public override void Write(Utf8JsonWriter writer, SpoolmanExternaldbMultiColorDirection? spoolmanExternaldbMultiColorDirection, JsonSerializerOptions options)
        {
            writer.WriteStringValue(spoolmanExternaldbMultiColorDirection?.ToString() ?? "null");
        }
    }
}
