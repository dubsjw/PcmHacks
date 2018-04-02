﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Flash411
{
    /// <summary>
    /// From the application's perspective, this class is the API to the vehicle.
    /// </summary>
    /// <remarks>
    /// Methods in this class are high-level operations like "get the VIN," or "read the contents of the EEPROM."
    /// </remarks>
    class Vehicle : IDisposable
    {
        /// <summary>
        /// The device we'll use to talk to the PCM.
        /// </summary>
        private Device device;

        /// <summary>
        /// This class knows how to generate message to send to the PCM.
        /// </summary>
        private MessageFactory messageFactory;

        /// <summary>
        /// This class knows how to parse the messages that come in from the PCM.
        /// </summary>
        private MessageParser messageParser;

        /// <summary>
        /// This is how we send user-friendly status messages and developer-oriented debug messages to the UI.
        /// </summary>
        private ILogger logger;

        public string DeviceDescription
        {
            get
            {
                return this.device.ToString();
            }
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        public Vehicle(
            Device device, 
            MessageFactory messageFactory,
            MessageParser messageParser,
            ILogger logger)
        {
            this.device = device;
            this.messageFactory = messageFactory;
            this.messageParser = messageParser;
            this.logger = logger;
        }

        /// <summary>
        /// Finalizer.
        /// </summary>
        ~Vehicle()
        {
            this.Dispose(false);
        }

        /// <summary>
        /// Implements IDisposable.Dispose.
        /// </summary>
        public void Dispose()
        {
            this.Dispose(true);
        }

        /// <summary>
        /// Part of the Dispose pattern.
        /// </summary>
        protected void Dispose(bool isDisposing)
        {
            if (this.device != null)
            {
                this.device.Dispose();
                this.device = null;
            }
        }

        /// <summary>
        /// Re-initialize the device.
        /// </summary>
        public async Task<bool> ResetConnection()
        {
            return await this.device.Initialize();
        }

        /// <summary>
        /// Query the PCM's VIN.
        /// </summary>
        public async Task<Response<string>> QueryVin()
        {
            Response<Message> response1 = await this.device.SendRequest(this.messageFactory.CreateVinRequest1());
            if (response1.Status != ResponseStatus.Success)
            {
                return Response.Create(response1.Status, "Unknown");
            }

            Response<Message> response2 = await this.device.SendRequest(this.messageFactory.CreateVinRequest2());
            if (response1.Status != ResponseStatus.Success)
            {
                return Response.Create(response1.Status, "Unknown");
            }

            Response<Message> response3 = await this.device.SendRequest(this.messageFactory.CreateVinRequest3());
            if (response1.Status != ResponseStatus.Success)
            {
                return Response.Create(response1.Status, "Unknown");
            }

            return this.messageParser.ParseVinResponses(response1.Value.GetBytes(), response2.Value.GetBytes(), response3.Value.GetBytes());
        }

        /// <summary>
        /// Query the PCM's Serial Number.
        /// </summary>
        public async Task<Response<string>> QuerySerial()
        {
            Response<Message> response1 = await this.device.SendRequest(this.messageFactory.CreateSerialRequest1());
            if (response1.Status != ResponseStatus.Success)
            {
                return Response.Create(response1.Status, "Unknown");
            }

            Response<Message> response2 = await this.device.SendRequest(this.messageFactory.CreateSerialRequest2());
            if (response2.Status != ResponseStatus.Success)
            {
                return Response.Create(response2.Status, "Unknown");
            }

            Response<Message> response3 = await this.device.SendRequest(this.messageFactory.CreateSerialRequest3());
            if (response3.Status != ResponseStatus.Success)
            {
                return Response.Create(response3.Status, "Unknown");
            }

            return this.messageParser.ParseSerialResponses(response1.Value, response2.Value, response3.Value);
        }

        /// <summary>
        /// Query the PCM's Broad Cast Code.
        /// </summary>
        public async Task<Response<string>> QueryBCC()
        {
            Response<Message> response = await this.device.SendRequest(this.messageFactory.CreateBCCRequest());
            if (response.Status != ResponseStatus.Success)
            {
                return Response.Create(response.Status, "Unknown");
            }

            return this.messageParser.ParseBCCresponse(response.Value.GetBytes());
        }

        /// <summary>
        /// Query the PCM's Manufacturer Enable Counter (MEC)
        /// </summary>
        public async Task<Response<string>> QueryMEC()
        {
            Response<Message> response = await this.device.SendRequest(this.messageFactory.CreateMECRequest());
            if (response.Status != ResponseStatus.Success)
            {
                return Response.Create(response.Status, "Unknown");
            }

            return this.messageParser.ParseMECresponse(response.Value.GetBytes());
        }

        /// <summary>
        /// Update the PCM's VIN
        /// </summary>
        /// <remarks>
        /// Requires that the PCM is already unlocked
        /// </remarks>
        public async Task<Response<bool>> UpdateVin(string vin)
        {
            if (vin.Length != 17) // should never happen, but....
            {
                this.logger.AddUserMessage("VIN " + vin + " is not 17 characters long!");
                return Response.Create(ResponseStatus.Error, false);
            }

            this.logger.AddUserMessage("Changing VIN to " + vin);

            byte[] bvin = Encoding.ASCII.GetBytes(vin);
            byte[] vin1 = new byte[6] { 0x00, bvin[0], bvin[1], bvin[2], bvin[3], bvin[4] };
            byte[] vin2 = new byte[6] { bvin[5], bvin[6], bvin[7], bvin[8], bvin[9], bvin[10] };
            byte[] vin3 = new byte[6] { bvin[11], bvin[12], bvin[13], bvin[14], bvin[15], bvin[16] };

            this.logger.AddUserMessage("Block 1");
            Response<bool> block1 = await WriteBlock(BlockId.Vin1, vin1);
            if (block1.Status != ResponseStatus.Success) return Response.Create(ResponseStatus.Error, false);
            this.logger.AddUserMessage("Block 2");
            Response<bool> block2 = await WriteBlock(BlockId.Vin2, vin2);
            if (block2.Status != ResponseStatus.Success) return Response.Create(ResponseStatus.Error, false);
            this.logger.AddUserMessage("Block 3");
            Response<bool> block3 = await WriteBlock(BlockId.Vin3, vin3);
            if (block3.Status != ResponseStatus.Success) return Response.Create(ResponseStatus.Error, false);

            return Response.Create(ResponseStatus.Success, true);
        }

        /// <summary>
        /// Query the PCM's operating system ID.
        /// </summary>
        /// <returns></returns>
        public async Task<Response<UInt32>> QueryOperatingSystemId()
        {
            Message request = this.messageFactory.CreateOperatingSystemIdReadRequest();
            var response = await this.device.SendRequest(request);
            if (response.Status != ResponseStatus.Success)
            {
                return Response.Create(response.Status, (UInt32)0);
            }

            return this.messageParser.ParseBlockUInt32(response.Value.GetBytes());
        }

        /// <summary>
        /// Query the PCM's Hardware ID.
        /// </summary>
        /// <remarks>
        /// Note that this is a software variable and my not match the hardware at all of the software runs.
        /// </remarks>
        /// <returns></returns>
        public async Task<Response<UInt32>> QueryHardwareId()
        {
            Message request = this.messageFactory.CreateHardwareIdReadRequest();
            var response = await this.device.SendRequest(request);
            if (response.Status != ResponseStatus.Success)
            {
                return Response.Create(response.Status, (UInt32)0);
            }

            return this.messageParser.ParseBlockUInt32(response.Value.GetBytes());
        }

        /// <summary>
        /// Query the PCM's Hardware ID.
        /// </summary>
        /// <remarks>
        /// Note that this is a software variable and my not match the hardware at all of the software runs.
        /// </remarks>
        /// <returns></returns>
        public async Task<Response<UInt32>> QueryCalibrationId()
        {
            Message request = this.messageFactory.CreateCalibrationIdReadRequest();
            var response = await this.device.SendRequest(request);
            if (response.Status != ResponseStatus.Success)
            {
                return Response.Create(response.Status, (UInt32)0);
            }

            return this.messageParser.ParseBlockUInt32(response.Value.GetBytes());
        }

        /// <summary>
        /// Unlock the PCM by requesting a 'seed' and then sending the corresponding 'key' value.
        /// </summary>
        public async Task<Response<bool>> UnlockEcu()
        {
            Message seedRequest = this.messageFactory.CreateSeedRequest();
            Response<Message> seedResponse = await this.device.SendRequest(seedRequest);
            if (seedResponse.Status != ResponseStatus.Success)
            {
                if (seedResponse.Status != ResponseStatus.UnexpectedResponse) Response.Create(ResponseStatus.Success, true);
                return Response.Create(seedResponse.Status, false);
            }

            Response<UInt16> seedValueResponse = this.messageParser.ParseSeed(seedResponse.Value.GetBytes());
            if (seedValueResponse.Status != ResponseStatus.Success)
            {
                return Response.Create(seedValueResponse.Status, false);
            }

            if (seedValueResponse.Value == 0x0000)
            {
                this.logger.AddUserMessage("PCM Unlock not required");
                return Response.Create(seedValueResponse.Status, true);
            }

            UInt16 key = KeyAlgorithm.GetKey(1, seedValueResponse.Value);

            Message unlockRequest = this.messageFactory.CreateUnlockRequest(key);
            Response<Message> unlockResponse = await this.device.SendRequest(unlockRequest);
            if (unlockResponse.Status != ResponseStatus.Success)
            {
                return Response.Create(unlockResponse.Status, false);
            }

            string errorMessage;
            Response<bool> result = this.messageParser.ParseUnlockResponse(unlockResponse.Value.GetBytes(), out errorMessage);
            if (errorMessage != null)
            {
                this.logger.AddUserMessage(errorMessage);
            }
            else
            {
                this.logger.AddUserMessage("PCM Unlocked");
            }

            return result;
        }

        /// <summary>
        /// Writes a block of data to the PCM
        /// Requires an unlocked PCM
        /// </summary>
        public async Task<Response<bool>> WriteBlock(byte block, byte[] data)
        {

            Message tx;
            Message ok = new Message(new byte[] { 0x6C, DeviceId.Tool, DeviceId.Pcm, 0x7B, block });

            switch (data.Length)
            {
                case 6:
                    tx = new Message(new byte[] { 0x6C, DeviceId.Pcm, DeviceId.Tool, 0x3B, block, data[0], data[1], data[2], data[3], data[4], data[5] });
                    break;
                default:
                    logger.AddDebugMessage("Cant write block size " + data.Length);
                    return Response.Create(ResponseStatus.Error, false);
            }

            Response<Message> rx = await this.device.SendRequest(tx);

            if (rx.Status != ResponseStatus.Success)
            {
                logger.AddUserMessage("Failed to write block " + block + ", communications failure");
                return Response.Create(ResponseStatus.Error, false);
            }

            if (!Utility.CompareArrays(rx.Value.GetBytes(), ok.GetBytes()))
            {
                logger.AddUserMessage("Failed to write block " + block + ", PCM rejected attempt");
                return Response.Create(ResponseStatus.Error, false);
            }

            logger.AddDebugMessage("Successful write to block " + block);
            return Response.Create(ResponseStatus.Success, true);
        }

        /// <summary>
        /// Read the full contents of the PCM.
        /// Assumes the PCM is unlocked an were ready to go
        /// </summary>
        public Task<Response<Stream>> ReadContents()
        {
            return Task.FromResult(new Response<Stream>(ResponseStatus.Success, (Stream)new MemoryStream(new byte[] { 0x01, 0x02, 0x03 })));
        }

        /// <summary>
        /// Replace the full contents of the PCM.
        /// </summary>
        public Task<bool> WriteContents(Stream stream)
        {
            return Task.FromResult(true);
        }

        /// <summary>
        /// Load the executable payload on the PCM from the supplied address, and execute it.
        /// </summary>
        public Task<bool> PCMExecute(byte[] payload, int address)
        {
            logger.AddDebugMessage("Going to load a " + payload.Length + " byte payload to 0x" + address.ToString("6X"));
            // Loop through the payload building and sending packets, highest first, execute on last
            for (int bytesremain = payload.Length; bytesremain > 0; bytesremain -= device.MaxSendSize)
            {
                bool exec = false;
                int length = device.MaxSendSize-12; //Headers use 10 bytes, sum uses 2 bytes
                int offset = bytesremain - length;

                if (offset<=0) // Is this the last packet?
                {
                    offset = 0;
                    length = bytesremain;
                    exec = true;
                }
                Message block = messageFactory.CreateBlockMessage(payload, offset, length, address + offset, exec);
            }

            return Task.FromResult(true);
        }

        /// <summary>
        /// Does everything required to switch to VPW 4x
        /// </summary>
        public async Task<bool> SetVPW4x(bool highspeed)
        {
            Message HighSpeedCheck = messageFactory.CreateHighSpeedCheck();
            Message HighSpeedOK = messageFactory.CreateHighSpeedOKResponse();
            Message BeginHighSpeed = messageFactory.CreateBeginHighSpeed();

            if (!device.Supports4X)
            {
                logger.AddUserMessage("This interface does not support VPW 4x");
                return false;
            }

            // PCM Pre-flight checks
            Response<Message> rx = await this.device.SendRequest(HighSpeedCheck);
            if (rx.Status != ResponseStatus.Success || !Utility.CompareArraysPart(rx.Value.GetBytes(), HighSpeedOK.GetBytes()))
            {
                logger.AddUserMessage("PCM is not allowing a switch to VPW 4x");
                return false;
            }

            // Request all devices on the bus to change speed to VPW 4x
            if (!await this.device.SendMessage(BeginHighSpeed))
            {
                logger.AddUserMessage("Could not transmit request to switch to VPW 4X");
                return false;
            }

            // Request the device to change
            await device.SetVPW4x(true);

            return true;
        }
    }
}
