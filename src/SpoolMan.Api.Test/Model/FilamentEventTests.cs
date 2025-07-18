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
    ///  Class for testing FilamentEvent
    /// </summary>
    /// <remarks>
    /// This file is automatically generated by OpenAPI Generator (https://openapi-generator.tech).
    /// Please update the test case below to test the model.
    /// </remarks>
    public class FilamentEventTests : IDisposable
    {
        // TODO uncomment below to declare an instance variable for FilamentEvent
        //private FilamentEvent instance;

        public FilamentEventTests()
        {
            // TODO uncomment below to create an instance of FilamentEvent
            //instance = new FilamentEvent();
        }

        public void Dispose()
        {
            // Cleanup when everything is done.
        }

        /// <summary>
        /// Test an instance of FilamentEvent
        /// </summary>
        [Fact]
        public void FilamentEventInstanceTest()
        {
            // TODO uncomment below to test "IsType" FilamentEvent
            //Assert.IsType<FilamentEvent>(instance);
        }

        /// <summary>
        /// Test the property 'Type'
        /// </summary>
        [Fact]
        public void TypeTest()
        {
            // TODO unit test for the property 'Type'
        }

        /// <summary>
        /// Test the property 'Resource'
        /// </summary>
        [Fact]
        public void ResourceTest()
        {
            // TODO unit test for the property 'Resource'
        }

        /// <summary>
        /// Test the property 'Date'
        /// </summary>
        [Fact]
        public void DateTest()
        {
            // TODO unit test for the property 'Date'
        }

        /// <summary>
        /// Test the property 'Payload'
        /// </summary>
        [Fact]
        public void PayloadTest()
        {
            // TODO unit test for the property 'Payload'
        }
    }
}
