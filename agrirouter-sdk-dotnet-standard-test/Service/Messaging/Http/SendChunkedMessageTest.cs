using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Agrirouter.Api.Definitions;
using Agrirouter.Api.Dto.Onboard;
using Agrirouter.Api.Service.Parameters;
using Agrirouter.Api.Service.Parameters.Inner;
using Agrirouter.Commons;
using Agrirouter.Impl.Service.Common;
using Agrirouter.Impl.Service.Messaging;
using Agrirouter.Request;
using Agrirouter.Request.Payload.Endpoint;
using Agrirouter.Test.Data;
using Agrirouter.Test.Helper;
using Google.Protobuf;
using Xunit;
using Xunit.Abstractions;

namespace Agrirouter.Test.Service.Messaging.Http
{
    /// <summary>
    ///     Functional tests.
    /// </summary>
    [Collection("Integrationtest")]
    public class SendChunkedMessageTest : AbstractIntegrationTestForCommunicationUnits
    {
        private readonly ITestOutputHelper _testOutputHelper;
        private static readonly HttpClient HttpClientForSender = HttpClientFactory.AuthenticatedHttpClient(Sender);

        private static readonly HttpClient
            HttpClientForRecipient = HttpClientFactory.AuthenticatedHttpClient(Recipient);

        public SendChunkedMessageTest(ITestOutputHelper testOutputHelper)
        {
            _testOutputHelper = testOutputHelper;
        }

        private void SetCapabilitiesForSender()
        {
            var capabilitiesServices =
                new CapabilitiesService(new HttpMessagingService(HttpClientForSender));
            var capabilitiesParameters = new CapabilitiesParameters
            {
                OnboardResponse = Sender,
                ApplicationId = Applications.CommunicationUnit.ApplicationId,
                CertificationVersionId = Applications.CommunicationUnit.CertificationVersionId,
                EnablePushNotifications = CapabilitySpecification.Types.PushNotification.Disabled,
                CapabilityParameters = new List<CapabilityParameter>()
            };

            var capabilitiesParameter = new CapabilityParameter
            {
                Direction = CapabilitySpecification.Types.Direction.SendReceive,
                TechnicalMessageType = TechnicalMessageTypes.ImgPng
            };
            capabilitiesParameters.CapabilityParameters.Add(capabilitiesParameter);

            capabilitiesParameter = new CapabilityParameter
            {
                Direction = CapabilitySpecification.Types.Direction.SendReceive,
                TechnicalMessageType = TechnicalMessageTypes.ImgBmp
            };
            capabilitiesParameters.CapabilityParameters.Add(capabilitiesParameter);

            capabilitiesParameter = new CapabilityParameter
            {
                Direction = CapabilitySpecification.Types.Direction.SendReceive,
                TechnicalMessageType = TechnicalMessageTypes.Iso11783TaskdataZip
            };
            capabilitiesParameters.CapabilityParameters.Add(capabilitiesParameter);

            capabilitiesServices.Send(capabilitiesParameters);

            Timer.WaitForTheAgrirouterToProcessTheMessage();

            var fetchMessageService = new FetchMessageService(HttpClientForSender);
            var fetch = fetchMessageService.Fetch(Sender);
            Assert.Single(fetch);

            var decodedMessage = DecodeMessageService.Decode(fetch[0].Command.Message);
            Assert.Equal(201, decodedMessage.ResponseEnvelope.ResponseCode);
        }

        private void SetCapabilitiesForRecipient()
        {
            var capabilitiesServices =
                new CapabilitiesService(new HttpMessagingService(HttpClientForRecipient));
            var capabilitiesParameters = new CapabilitiesParameters
            {
                OnboardResponse = Recipient,
                ApplicationId = Applications.CommunicationUnit.ApplicationId,
                CertificationVersionId = Applications.CommunicationUnit.CertificationVersionId,
                EnablePushNotifications = CapabilitySpecification.Types.PushNotification.Disabled,
                CapabilityParameters = new List<CapabilityParameter>()
            };

            var capabilitiesParameter = new CapabilityParameter
            {
                Direction = CapabilitySpecification.Types.Direction.SendReceive,
                TechnicalMessageType = TechnicalMessageTypes.ImgPng
            };

            capabilitiesParameters.CapabilityParameters.Add(capabilitiesParameter);
            capabilitiesServices.Send(capabilitiesParameters);

            Timer.WaitForTheAgrirouterToProcessTheMessage();

            var fetchMessageService = new FetchMessageService(HttpClientForRecipient);
            var fetch = fetchMessageService.Fetch(Recipient);
            Assert.Single(fetch);

            var decodedMessage = DecodeMessageService.Decode(fetch[0].Command.Message);
            Assert.Equal(201, decodedMessage.ResponseEnvelope.ResponseCode);
        }

        private static OnboardResponse Sender =>
            OnboardResponseIntegrationService.Read(Identifier.Http.CommunicationUnit.Sender);

        private static OnboardResponse Recipient =>
            OnboardResponseIntegrationService.Read(Identifier.Http.CommunicationUnit.Recipient);

        private static string SensorAlternateIdForIOTool => "37cd61d1-76eb-4145-a735-c938d05a32d8";


        [Fact]
        public void GivenValidMessageContentWhenSendingMessageToSingleRecipientThenTheMessageShouldBeDelivered()
        {
            // Description of the messaging process.

            // 1. Set all capabilities for each endpoint - this is done once, not each time.
            SetCapabilitiesForSender();
            SetCapabilitiesForRecipient();

            // 2. Set routes within the UI - this is done once, not each time.
            // Done manually, not API interaction necessary.

            // 3. Add message header and message payloads.
            var headerParameters = new MessageHeaderParameters
            {
                Metadata = new Metadata
                {
                    FileName = "my_personal_filename.bmp"
                },
                Mode = RequestEnvelope.Types.Mode.Direct,
                Recipients = new List<string> { SensorAlternateIdForIOTool },
                TechnicalMessageType = TechnicalMessageTypes.ImgBmp
            };
            var payloadParameters = new MessagePayloadParameters
            {
                Value = ByteString.CopyFrom(DataProvider.ReadLargeBmp()),
                TypeUrl = TechnicalMessageTypes.Empty
            };

            // 4. Chunk message content before sending it.
            var messageParameterTuples =
                EncodeMessageService.ChunkAndBase64EncodeEachChunk(headerParameters, payloadParameters);
            var encodedMessages = (from messageParameterTuple in messageParameterTuples
                let messageHeaderParameters = messageParameterTuple.MessageHeaderParameters
                let messagePayloadParameters = messageParameterTuple.MessagePayloadParameters
                select EncodeMessageService.Encode(messageHeaderParameters, messagePayloadParameters)).ToList();

            // 5. Send messages from sender to recipient.
            var sendMessageService =
                new SendDirectMessageService(new HttpMessagingService(HttpClientForSender));
            var messagingParameters = new MessagingParameters()
            {
                OnboardResponse = Sender,
                ApplicationMessageId = MessageIdService.ApplicationMessageId(),
                EncodedMessages = encodedMessages
            };
            sendMessageService.Send(messagingParameters);

            // 6. Let the AR handle the message - this can take up to multiple seconds before receiving the ACK.
            Timer.WaitForTheAgrirouterToProcessTheMessage();

            // 7. Fetch and analyze the ACK from the AR.
            var fetchMessageService = new FetchMessageService(HttpClientForSender);
            var fetch = fetchMessageService.Fetch(Sender);
            Assert.Equal(4, fetch.Count);

            fetch.ForEach(response =>
            {
                var decodedMessage = DecodeMessageService.Decode(response.Command.Message);
                Assert.Equal(201, decodedMessage.ResponseEnvelope.ResponseCode);
            });
        }
    }
}