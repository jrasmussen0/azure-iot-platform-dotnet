// <copyright file="DevicesTest.cs" company="3M">
// Copyright (c) 3M. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.Devices;
using Microsoft.Azure.Devices.Shared;
using Mmm.Iot.Common.Services.Exceptions;
using Mmm.Iot.Common.Services.External.AsaManager;
using Mmm.Iot.Common.TestHelpers;
using Mmm.Iot.IoTHubManager.Services.Helpers;
using Mmm.Iot.IoTHubManager.Services.Models;
using Moq;
using Xunit;
using AuthenticationType = Mmm.Iot.IoTHubManager.Services.Models.AuthenticationType;

namespace Mmm.Iot.IoTHubManager.Services.Test
{
    public class DevicesTest
    {
        private readonly IDevices devices;
        private readonly Mock<RegistryManager> registryMock;
        private readonly string ioTHubHostName = "ioTHubHostName";
        private readonly Mock<ITenantConnectionHelper> mockTenantHelper;
        private readonly Mock<IAsaManagerClient> mockAsaManager;

        public DevicesTest()
        {
            this.registryMock = new Mock<RegistryManager>();
            this.mockTenantHelper = new Mock<ITenantConnectionHelper>();
            this.mockTenantHelper.Setup(e => e.GetIotHubName()).Returns(this.ioTHubHostName);
            this.mockTenantHelper.Setup(e => e.GetRegistry()).Returns(this.registryMock.Object);
            MockIdentity.MockClaims("one");

            this.mockAsaManager = new Mock<IAsaManagerClient>();

            this.mockAsaManager
                .Setup(x => x.BeginDeviceGroupsConversionAsync())
                .ReturnsAsync(new BeginConversionApiModel());

            this.devices = new Devices(this.mockTenantHelper.Object, this.ioTHubHostName, this.mockAsaManager.Object);
        }

        [Theory]
        [Trait(Constants.Type, Constants.UnitTest)]
        [InlineData("", "", true)]
        [InlineData("asdf", "", true)]
        [InlineData("", "qwer", true)]
        [InlineData("asdf", "qwer", false)]
        public async Task GetModuleTwinTest(string deviceId, string moduleId, bool throwsException)
        {
            if (throwsException)
            {
                // Act & Assert
                await Assert.ThrowsAsync<InvalidInputException>(async () =>
                    await this.devices.GetModuleTwinAsync(deviceId, moduleId));
            }
            else
            {
                // Arrange
                this.registryMock
                    .Setup(x => x.GetTwinAsync(deviceId, moduleId))
                    .ReturnsAsync(DevicesTest.CreateTestTwin(0));

                // Act
                var twinSvcModel = await this.devices.GetModuleTwinAsync(deviceId, moduleId);

                // Assert
                Assert.Equal("value0", twinSvcModel.ReportedProperties["test"].ToString());
                Assert.Equal("value0", twinSvcModel.DesiredProperties["test"].ToString());
            }
        }

        [Theory]
        [Trait(Constants.Type, Constants.UnitTest)]
        [InlineData("", 5)]
        [InlineData("2", 5)]
        [InlineData("6", 5)]
        public async Task TwinByQueryContinuationTest(string continuationToken, int numResults)
        {
            // Arrange
            this.registryMock
                .Setup(x => x.CreateQuery(It.IsAny<string>()))
                .Returns(new ResultQuery(numResults));

            // Act
            var queryResult = await this.devices.GetModuleTwinsByQueryAsync(string.Empty, continuationToken);

            // Assert
            Assert.Equal("continuationToken", queryResult.ContinuationToken);

            var startIndex = string.IsNullOrEmpty(continuationToken) ? 0 : int.Parse(continuationToken);
            var total = Math.Max(0, numResults - startIndex);
            Assert.Equal(total, queryResult.Items.Count);

            for (int i = 0; i < total; i++)
            {
                var expectedValue = "value" + (i + startIndex);
                Assert.Equal(expectedValue, queryResult.Items[i].ReportedProperties["test"].ToString());
                Assert.Equal(expectedValue, queryResult.Items[i].DesiredProperties["test"].ToString());
            }
        }

        [Theory]
        [Trait(Constants.Type, Constants.UnitTest)]
        [InlineData("", "SELECT * FROM devices.modules")]
        [InlineData("deviceId='test'", "SELECT * FROM devices.modules where deviceId='test'")]
        public async Task GetTwinByQueryTest(string query, string queryToMatch)
        {
            // Arrange
            this.registryMock
                .Setup(x => x.CreateQuery(queryToMatch))
                .Returns(new ResultQuery(3));

            // Act
            var queryResult = await this.devices.GetModuleTwinsByQueryAsync(query, string.Empty);

            // Assert
            Assert.Equal("continuationToken", queryResult.ContinuationToken);
            Assert.Equal(3, queryResult.Items.Count);
        }

        [Fact]
        [Trait(Constants.Type, Constants.UnitTest)]
        public async Task GetEdgeDeviceTest()
        {
            // Arrange
            var nonEdgeDevice = "nonEdgeDevice";
            var edgeDevice = "edgeDevice";
            var edgeDeviceFromTwin = "edgeDeviceFromTwin";

            this.registryMock
                .Setup(x => x.CreateQuery(It.IsAny<string>()))
                .Returns(new ResultQuery(0));

            this.registryMock
                .Setup(x => x.GetTwinAsync(nonEdgeDevice))
                .ReturnsAsync(DevicesTest.CreateTestTwin(0));
            this.registryMock
                .Setup(x => x.GetTwinAsync(edgeDevice))
                .ReturnsAsync(DevicesTest.CreateTestTwin(1));
            this.registryMock
                .Setup(x => x.GetTwinAsync(edgeDeviceFromTwin))
                .ReturnsAsync(DevicesTest.CreateTestTwin(2, true));

            this.registryMock
                .Setup(x => x.GetDeviceAsync(nonEdgeDevice))
                .ReturnsAsync(DevicesTest.CreateTestDevice("nonEdgeDevice", false));
            this.registryMock
                .Setup(x => x.GetDeviceAsync(edgeDevice))
                .ReturnsAsync(DevicesTest.CreateTestDevice("edgeDevice", true));
            this.registryMock
                .Setup(x => x.GetDeviceAsync(edgeDeviceFromTwin))
                .ReturnsAsync(DevicesTest.CreateTestDevice("edgeDeviceFromTwin", false));

            // Act
            var dvc1 = await this.devices.GetAsync(nonEdgeDevice);
            var dvc2 = await this.devices.GetAsync(edgeDevice);
            var dvc3 = await this.devices.GetAsync(edgeDeviceFromTwin);

            // Assert
            Assert.False(dvc1.IsEdgeDevice, "Non-edge device reporting edge device");
            Assert.True(dvc2.IsEdgeDevice, "Edge device reported not edge device");

            // When using getDevices method which is deprecated it doesn't return IsEdgeDevice
            // capabilities properly so we support grabbing this from the device twin as well.
            Assert.True(dvc3.IsEdgeDevice, "Edge device from twin reporting not edge device");
        }

        [Fact]
        [Trait(Constants.Type, Constants.UnitTest)]
        public async Task TestConnectedEdgeDevice()
        {
            // Arrange
            var twins = this.CreateTestListOfTwins();
            var connectedTwins = this.CreateTestListOfTwins();
            connectedTwins.RemoveAt(3);

            this.registryMock
                .Setup(x => x.CreateQuery(It.Is<string>(s => s.Equals("SELECT * FROM devices"))))
                .Returns(new ResultQuery(twins));

            // Set only 3 of the devices to be marked as connected
            // The first two are non-edge devices so it shouldn't be listed
            // as connected in the result
            this.registryMock
                .Setup(x => x.CreateQuery(It.Is<string>(s => s.Equals("SELECT * FROM devices.modules where connectionState = 'Connected'"))))
                .Returns(new ResultQuery(connectedTwins));

            // Act
            var allDevices = await this.devices.GetListAsync(string.Empty, string.Empty);

            // Assert
            Assert.Equal(4, allDevices.Items.Count);
            Assert.False(allDevices.Items[0].Connected || allDevices.Items[1].Connected);
            Assert.True(allDevices.Items[2].Connected);
            Assert.False(allDevices.Items[3].Connected);
        }

        [Theory]
        [Trait(Constants.Type, Constants.UnitTest)]
        [InlineData("SelfSigned")]
        [InlineData("CertificateAuthority")]
        public async Task InvalidAuthenticationTypeForEdgeDevice(string authTypeString)
        {
            // Arrange
            var authType = Enum.Parse<AuthenticationType>(authTypeString);

            var auth = new AuthenticationMechanismServiceModel()
            {
                AuthenticationType = authType,
            };

            DeviceServiceModel model = new DeviceServiceModel(
                etag: "etag",
                id: "deviceId",
                c2DMessageCount: 0,
                lastActivity: DateTime.Now,
                connected: true,
                enabled: true,
                isEdgeDevice: true,
                lastStatusUpdated: DateTime.Now,
                twin: null,
                ioTHubHostName: this.ioTHubHostName,
                authentication: auth);

            // Act & Assert
            await Assert.ThrowsAsync<InvalidInputException>(async () =>
                await this.devices.CreateAsync(model));
        }

        [Fact]
        [Trait(Constants.Type, Constants.UnitTest)]
        public async Task UpdateDeviceThrowsResourceNotFound()
        {
            this.registryMock
                .Setup(x => x.GetDeviceAsync(
                    It.IsAny<string>()))
                .ReturnsAsync((Device)null);

            var model = new DeviceServiceModel(
                CreateTestDevice("deviceId", false),
                CreateTestTwin(0, false),
                "iotHubHostName",
                false);

            await Assert.ThrowsAsync<ResourceNotFoundException>(async () =>
                await this.devices.UpdateAsync(model, null));
        }

        private static Twin CreateTestTwin(int valueToReport, bool isEdgeDevice = false)
        {
            var twin = new Twin()
            {
                Properties = new TwinProperties(),
                Capabilities = isEdgeDevice ? new DeviceCapabilities() { IotEdge = true } : null,
            };
            twin.DeviceId = $"device{valueToReport}";
            twin.Properties.Reported = new TwinCollection("{\"test\":\"value" + valueToReport + "\"}");
            twin.Properties.Desired = new TwinCollection("{\"test\":\"value" + valueToReport + "\"}");
            return twin;
        }

        private static Device CreateTestDevice(string deviceId, bool isEdgeDevice)
        {
            var dvc = new Device(deviceId)
            {
                Authentication = new AuthenticationMechanism
                {
                    Type = Microsoft.Azure.Devices.AuthenticationType.Sas,
                    SymmetricKey = new SymmetricKey
                    {
                        PrimaryKey = Convert.ToBase64String(Encoding.UTF8.GetBytes("SomeTestPrimaryKey")),
                        SecondaryKey = Convert.ToBase64String(Encoding.UTF8.GetBytes("SomeTestSecondaryKey")),
                    },
                },
                Capabilities = isEdgeDevice ? new DeviceCapabilities() { IotEdge = true } : null,
            };
            return dvc;
        }

        private List<Twin> CreateTestListOfTwins()
        {
            return new List<Twin>()
            {
                DevicesTest.CreateTestTwin(0, false),
                DevicesTest.CreateTestTwin(1, false),
                DevicesTest.CreateTestTwin(2, true),
                DevicesTest.CreateTestTwin(3, true),
            };
        }
    }
}