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
    ///  Class for testing HTTPValidationError
    /// </summary>
    /// <remarks>
    /// This file is automatically generated by OpenAPI Generator (https://openapi-generator.tech).
    /// Please update the test case below to test the model.
    /// </remarks>
    public class HTTPValidationErrorTests : IDisposable
    {
        // TODO uncomment below to declare an instance variable for HTTPValidationError
        //private HTTPValidationError instance;

        public HTTPValidationErrorTests()
        {
            // TODO uncomment below to create an instance of HTTPValidationError
            //instance = new HTTPValidationError();
        }

        public void Dispose()
        {
            // Cleanup when everything is done.
        }

        /// <summary>
        /// Test an instance of HTTPValidationError
        /// </summary>
        [Fact]
        public void HTTPValidationErrorInstanceTest()
        {
            // TODO uncomment below to test "IsType" HTTPValidationError
            //Assert.IsType<HTTPValidationError>(instance);
        }

        /// <summary>
        /// Test the property 'Detail'
        /// </summary>
        [Fact]
        public void DetailTest()
        {
            // TODO unit test for the property 'Detail'
        }
    }
}
