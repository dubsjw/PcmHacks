﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PcmHacking
{
    /// <summary>
    /// From the application's perspective, this class is the API to the vehicle.
    /// </summary>
    /// <remarks>
    /// Methods in this class are high-level operations like "get the VIN," or "read the contents of the EEPROM."
    /// </remarks>
    public partial class Vehicle : IDisposable
    {
        /// <summary>
        /// Read the full contents of the PCM.
        /// Assumes the PCM is unlocked an were ready to go
        /// </summary>
        public async Task<Response<Stream>> ReadContents(PcmInfo info, CancellationToken cancellationToken)
        {
            try
            {
                await notifier.Notify();
                this.device.ClearMessageQueue();

                // switch to 4x, if possible. But continue either way.
                // if the vehicle bus switches but the device does not, the bus will need to time out to revert back to 1x, and the next steps will fail.
                if (!await this.VehicleSetVPW4x(VpwSpeed.FourX, notifier))
                {
                    this.logger.AddUserMessage("Stopping here because we were unable to switch to 4X.");
                    return Response.Create(ResponseStatus.Error, (Stream)null);
                }

                await notifier.Notify();

                // execute read kernel
                Response<byte[]> response = await LoadKernelFromFile("read-kernel.bin");
                if (response.Status != ResponseStatus.Success)
                {
                    logger.AddUserMessage("Failed to load kernel from file.");
                    return new Response<Stream>(response.Status, null);
                }
                
                if (cancellationToken.IsCancellationRequested)
                {
                    return Response.Create(ResponseStatus.Cancelled, (Stream)null);
                }

                await notifier.Notify();

                // TODO: instead of this hard-coded 0xFF9150, get the base address from the PcmInfo object.
                // TODO: choose kernel at run time? Because now it's FF8000...
                if (!await PCMExecute(response.Value, 0xFF8000, notifier, cancellationToken))
                {
                    logger.AddUserMessage("Failed to upload kernel to PCM");

                    return new Response<Stream>(
                        cancellationToken.IsCancellationRequested ? ResponseStatus.Cancelled : ResponseStatus.Error, 
                        null);
                }

                logger.AddUserMessage("kernel uploaded to PCM succesfully. Requesting data...");

                await this.device.SetTimeout(TimeoutScenario.ReadMemoryBlock);

                int startAddress = info.ImageBaseAddress;
                int endAddress = info.ImageBaseAddress + info.ImageSize;
                int bytesRemaining = info.ImageSize;
                int blockSize = this.device.MaxReceiveSize - 10 - 2; // allow space for the header and block checksum

                byte[] image = new byte[info.ImageSize];

                while (startAddress < endAddress)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return Response.Create(ResponseStatus.Cancelled, (Stream)null);
                    }

                    // The read kernel needs a short message here for reasons unknown. Without it, it will RX 2 messages then drop one.
                    await notifier.ForceNotify();

                    if (startAddress + blockSize > endAddress)
                    {
                        blockSize = endAddress - startAddress;
                    }

                    if (blockSize < 1)
                    {
                        this.logger.AddUserMessage("Image download complete");
                        break;
                    }
                    
                    if (!await TryReadBlock(image, blockSize, startAddress, cancellationToken))
                    {
                        this.logger.AddUserMessage(
                            string.Format(
                                "Unable to read block from {0} to {1}",
                                startAddress,
                                (startAddress + blockSize) - 1));
                        return new Response<Stream>(ResponseStatus.Error, null);
                    }

                    startAddress += blockSize;
                }

                await this.Cleanup(); // Not sure why this does not get called in the finally block on successfull read?

                MemoryStream stream = new MemoryStream(image);
                return new Response<Stream>(ResponseStatus.Success, stream);
            }
            catch(Exception exception)
            {
                this.logger.AddUserMessage("Something went wrong. " + exception.Message);
                this.logger.AddDebugMessage(exception.ToString());
                return new Response<Stream>(ResponseStatus.Error, null);
            }
            finally
            {
                // Sending the exit command at both speeds and revert to 1x.
                await this.Cleanup();
            }
        }

        /// <summary>
        /// Try to read a block of PCM memory.
        /// </summary>
        private async Task<bool> TryReadBlock(byte[] image, int length, int startAddress, CancellationToken cancellationToken)
        {
            this.logger.AddDebugMessage(string.Format("Reading from {0} / 0x{0:X}, length {1} / 0x{1:X}", startAddress, length));

            for (int sendAttempt = 1; sendAttempt <= MaxSendAttempts; sendAttempt++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                Message message = this.protocol.CreateReadRequest(startAddress, length);

                if (!await this.device.SendMessage(message))
                {
                    this.logger.AddDebugMessage("Unable to send read request.");
                    continue;
                }
                
                for (int receiveAttempt = 1; receiveAttempt <= MaxReceiveAttempts; receiveAttempt++)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    Message payloadMessage = await this.device.ReceiveMessage();
                    if (payloadMessage == null)
                    {
                        this.logger.AddDebugMessage("No payload following read request.");
                        continue;
                    }

                    this.logger.AddDebugMessage("Processing message");

                    Response<byte[]> payloadResponse = this.protocol.ParsePayload(payloadMessage, length, startAddress);
                    if (payloadResponse.Status != ResponseStatus.Success)
                    {
                        this.logger.AddDebugMessage("Not a valid payload message or bad checksum");
                        continue;
                    }

                    byte[] payload = payloadResponse.Value;
                    Buffer.BlockCopy(payload, 0, image, startAddress, length);

                    int percentDone = (startAddress * 100) / image.Length;
                    this.logger.AddUserMessage(string.Format("Recieved block starting at {0} / 0x{0:X}. {1}%", startAddress, percentDone));

                    return true;
                }
            }

            return false;
        }
    }
}
