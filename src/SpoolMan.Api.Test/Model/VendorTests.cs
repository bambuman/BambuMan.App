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
    ///  Class for testing Vendor
    /// </summary>
    /// <remarks>
    /// This file is automatically generated by OpenAPI Generator (https://openapi-generator.tech).
    /// Please update the test case below to test the model.
    /// </remarks>
    public class VendorTests : IDisposable
    {
        // TODO uncomment below to declare an instance variable for Vendor
        //private Vendor instance;

        public VendorTests()
        {
            // TODO uncomment below to create an instance of Vendor
            //instance = new Vendor();
        }

        public void Dispose()
        {
            // Cleanup when everything is done.
        }

        /// <summary>
        /// Test an instance of Vendor
        /// </summary>
        [Fact]
        public void VendorInstanceTest()
        {
            // TODO uncomment below to test "IsType" Vendor
            //Assert.IsType<Vendor>(instance);
        }

        /// <summary>
        /// Test the property 'Id'
        /// </summary>
        [Fact]
        public void IdTest()
        {
            // TODO unit test for the property 'Id'
        }

        /// <summary>
        /// Test the property 'Registered'
        /// </summary>
        [Fact]
        public void RegisteredTest()
        {
            // TODO unit test for the property 'Registered'
        }

        /// <summary>
        /// Test the property 'Name'
        /// </summary>
        [Fact]
        public void NameTest()
        {
            // TODO unit test for the property 'Name'
        }

        /// <summary>
        /// Test the property 'Extra'
        /// </summary>
        [Fact]
        public void ExtraTest()
        {
            // TODO unit test for the property 'Extra'
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
        /// Test the property 'EmptySpoolWeight'
        /// </summary>
        [Fact]
        public void EmptySpoolWeightTest()
        {
            // TODO unit test for the property 'EmptySpoolWeight'
        }

        /// <summary>
        /// Test the property 'ExternalId'
        /// </summary>
        [Fact]
        public void ExternalIdTest()
        {
            // TODO unit test for the property 'ExternalId'
        }
    }
}
