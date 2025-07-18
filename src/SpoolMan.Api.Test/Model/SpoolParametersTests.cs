/*
 * Spoolman REST API v1
 *
 *      REST API for Spoolman.      The API is served on the path `/api/v1/`.      Some endpoints also serve a websocket on the same path. The websocket is used to listen for changes to the data     that the endpoint serves. The websocket messages are JSON objects. Additionally, there is a root-level websocket     endpoint that listens for changes to any data in the database.     
 *
 * The version of the OpenAPI document: 1.0.0
 * Generated by: https://github.com/openapitools/openapi-generator.git
 */


using Xunit;

using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using SpoolMan.Api.Model;
using SpoolMan.Api.Client;
using System.Reflection;

namespace SpoolMan.Api.Test.Model
{
    /// <summary>
    ///  Class for testing SpoolParameters
    /// </summary>
    /// <remarks>
    /// This file is automatically generated by OpenAPI Generator (https://openapi-generator.tech).
    /// Please update the test case below to test the model.
    /// </remarks>
    public class SpoolParametersTests : IDisposable
    {
        // TODO uncomment below to declare an instance variable for SpoolParameters
        //private SpoolParameters instance;

        public SpoolParametersTests()
        {
            // TODO uncomment below to create an instance of SpoolParameters
            //instance = new SpoolParameters();
        }

        public void Dispose()
        {
            // Cleanup when everything is done.
        }

        /// <summary>
        /// Test an instance of SpoolParameters
        /// </summary>
        [Fact]
        public void SpoolParametersInstanceTest()
        {
            // TODO uncomment below to test "IsType" SpoolParameters
            //Assert.IsType<SpoolParameters>(instance);
        }

        /// <summary>
        /// Test the property 'FilamentId'
        /// </summary>
        [Fact]
        public void FilamentIdTest()
        {
            // TODO unit test for the property 'FilamentId'
        }

        /// <summary>
        /// Test the property 'FirstUsed'
        /// </summary>
        [Fact]
        public void FirstUsedTest()
        {
            // TODO unit test for the property 'FirstUsed'
        }

        /// <summary>
        /// Test the property 'LastUsed'
        /// </summary>
        [Fact]
        public void LastUsedTest()
        {
            // TODO unit test for the property 'LastUsed'
        }

        /// <summary>
        /// Test the property 'Price'
        /// </summary>
        [Fact]
        public void PriceTest()
        {
            // TODO unit test for the property 'Price'
        }

        /// <summary>
        /// Test the property 'InitialWeight'
        /// </summary>
        [Fact]
        public void InitialWeightTest()
        {
            // TODO unit test for the property 'InitialWeight'
        }

        /// <summary>
        /// Test the property 'SpoolWeight'
        /// </summary>
        [Fact]
        public void SpoolWeightTest()
        {
            // TODO unit test for the property 'SpoolWeight'
        }

        /// <summary>
        /// Test the property 'RemainingWeight'
        /// </summary>
        [Fact]
        public void RemainingWeightTest()
        {
            // TODO unit test for the property 'RemainingWeight'
        }

        /// <summary>
        /// Test the property 'UsedWeight'
        /// </summary>
        [Fact]
        public void UsedWeightTest()
        {
            // TODO unit test for the property 'UsedWeight'
        }

        /// <summary>
        /// Test the property 'Location'
        /// </summary>
        [Fact]
        public void LocationTest()
        {
            // TODO unit test for the property 'Location'
        }

        /// <summary>
        /// Test the property 'LotNr'
        /// </summary>
        [Fact]
        public void LotNrTest()
        {
            // TODO unit test for the property 'LotNr'
        }

        /// <summary>
        /// Test the property 'Comment'
        /// </summary>
        [Fact]
        public void CommentTest()
        {
            // TODO unit test for the property 'Comment'
        }

        /// <summary>
        /// Test the property 'Archived'
        /// </summary>
        [Fact]
        public void ArchivedTest()
        {
            // TODO unit test for the property 'Archived'
        }

        /// <summary>
        /// Test the property 'Extra'
        /// </summary>
        [Fact]
        public void ExtraTest()
        {
            // TODO unit test for the property 'Extra'
        }
    }
}
