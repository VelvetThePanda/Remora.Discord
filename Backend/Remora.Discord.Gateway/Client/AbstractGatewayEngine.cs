//
//  AbstractGatewayEngine.cs
//
//  Author:
//       Jarl Gullberg <jarl.gullberg@gmail.com>
//
//  Copyright (c) 2017 Jarl Gullberg
//
//  This program is free software: you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
//
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public License
//  along with this program.  If not, see <http://www.gnu.org/licenses/>.
//

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Remora.Discord.API.Abstractions.Gateway;
using Remora.Discord.API.Abstractions.Gateway.Bidirectional;
using Remora.Discord.API.Abstractions.Gateway.Events;
using Remora.Discord.API.Gateway.Bidirectional;
using Remora.Discord.Gateway.Results;
using Remora.Discord.Gateway.Transport;
using Remora.Results;
using static System.Net.WebSockets.WebSocketCloseStatus;

namespace Remora.Discord.Gateway
{
    /// <summary>
    /// Acts as the base implementation of a gateway client. This base manages heartbeats, reconnections, and session
    /// resumes without any implementation-specific knowledge.
    /// </summary>
    /// <typeparam name="TPayload">The payload type to utilize.</typeparam>
    /// <typeparam name="THeartbeat">The heartbeat type to utilize.</typeparam>
    /// <typeparam name="THeartbeatAcknowledge">The heartbeat acknowledgement type to utilize.</typeparam>
    /// <typeparam name="TResume">The session resumption type to utilize.</typeparam>
    /// <typeparam name="TReady">The connection readiness type to utilize.</typeparam>
    public abstract class AbstractGatewayEngine<TPayload, THeartbeat, THeartbeatAcknowledge, TResume, TReady>
    {
        /// <summary>
        /// Holds the payload transport service.
        /// </summary>
        private readonly IPayloadTransportService _transportService;

        /// <summary>
        /// Holds payloads that have been submitted by the application, but have not yet been sent to the gateway.
        /// </summary>
        private readonly ConcurrentQueue<TPayload> _payloadsToSend;

        /// <summary>
        /// Holds payloads that have been received by the gateway, but not yet distributed to the application.
        /// </summary>
        private readonly ConcurrentQueue<TPayload> _receivedPayloads;

        /// <summary>
        /// Holds the interval at which heartbeats should be sent.
        /// </summary>
        private TimeSpan _heartbeatInterval;

        /// <summary>
        /// Holds the time when the last heartbeat acknowledgement was received, using
        /// <see cref="DateTime.ToBinary()"/>.
        /// </summary>
        private long _lastReceivedHeartbeatAck;

        /// <summary>
        /// Gets the status of the engine.
        /// </summary>
        public GatewayEngineStatus Status { get; }

        /// <summary>
        /// Starts and runs the gateway engine.
        /// </summary>
        /// <remarks>
        /// This task will not complete until cancelled (or faulted), maintaining the connection for the duration of it.
        ///
        /// If the gateway engine encounters a fatal problem during the execution of this task, it will return with a
        /// failed result. If a shutdown is requested, it will gracefully terminate the connection and return a
        /// successful result.
        /// </remarks>
        /// <param name="ct">A token by which the caller can request this method to stop.</param>
        /// <returns>A gateway connection result which may or may not have succeeded.</returns>
        public async Task<Result> RunAsync(CancellationToken ct = default)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var internalCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    var sendTask = ClientEngineSenderAsync(internalCancellationSource.Token);
                    var receiveTask = ClientEngineReceiverAsync(internalCancellationSource.Token);
                }
                catch (TaskCanceledException)
                {
                    return Result.FromSuccess();
                }
            }
        }

        /// <summary>
        /// Submits a command for transmission to the gateway.
        /// </summary>
        /// <param name="command">The gateway command.</param>
        /// <typeparam name="TCommand">The command type.</typeparam>
        public void SubmitCommand<TCommand>(TCommand command)
        {
            _payloadsToSend.Enqueue(CreatePayload(command));
        }

        /// <summary>
        /// Creates a payload that contains the given data packet.
        /// </summary>
        /// <param name="data">The data packet.</param>
        /// <typeparam name="TData">The data packet type.</typeparam>
        /// <returns>A payload that contains the given data packet.</returns>
        protected abstract TPayload CreatePayload<TData>(TData data);

        /// <summary>
        /// Creates a heartbeat instance that is used to keep the connection alive.
        /// </summary>
        /// <returns>The heartbeat.</returns>
        protected abstract THeartbeat CreateHeartbeat();

        /// <summary>
        /// Creates a heartbeat acknowledgement that is used to acknowledge a request to keep the connection alive.
        /// </summary>
        /// <returns>The heartbeat acknowledgement.</returns>
        protected abstract THeartbeatAcknowledge CreateHeartbeatAcknowledge();

        /// <summary>
        /// Creates a resumption request that is used to continue an interrupted session.
        /// </summary>
        /// <returns>The resumption request.</returns>
        protected abstract TResume CreateResume();

        /// <summary>
        /// Determines the action to take based on a received payload.
        /// </summary>
        /// <param name="payload">The payload.</param>
        /// <returns>The action to take.</returns>
        protected abstract Result<PayloadAction> DetermineAction(TPayload payload);

        /// <summary>
        /// This method acts as the main entrypoint for the gateway receiver task. It processes payloads that are
        /// sent from the gateway to the application, submitting them to it.
        /// </summary>
        /// <param name="ct">A token for requests to disconnect the socket.</param>
        /// <returns>A receiver result which may or may not have been successful. A failed result indicates that
        /// something has gone wrong when receiving a payload, and that the connection has been deemed nonviable. A
        /// nonviable connection should be either terminated, reestablished, or resumed as appropriate.</returns>
        private async Task<Result<PayloadAction>> ClientEngineReceiverAsync(CancellationToken ct = default)
        {
            await Task.Yield();

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var receivedPayload = await _transportService.ReceivePayloadAsync<TPayload>(ct);
                    if (!receivedPayload.IsSuccess)
                    {
                        // Normal closures are okay
                        if (receivedPayload.Error is GatewayWebSocketError { CloseStatus: NormalClosure })
                        {
                            return PayloadAction.Reconnect;
                        }

                        return Result<PayloadAction>.FromError(receivedPayload);
                    }

                    // Update the ack timestamp
                    if (receivedPayload.Entity is IPayload<IHeartbeatAcknowledge>)
                    {
                        Interlocked.Exchange(ref _lastReceivedHeartbeatAck, DateTime.UtcNow.ToBinary());
                    }

                    // Signal the governor task that a reconnection is requested, if necessary.
                    switch (receivedPayload.Entity)
                    {
                        case THeartbeat:
                        {
                            SubmitCommand(CreateHeartbeatAcknowledge());
                            continue;
                        }
                        default:
                        {
                            // Enqueue the payload for dispatch
                            _receivedPayloads.Enqueue(receivedPayload.Entity);

                            var determineAction = DetermineAction(receivedPayload.Entity);
                            if (!determineAction.IsSuccess)
                            {
                                return Result<PayloadAction>.FromError(determineAction);
                            }

                            switch (determineAction.Entity)
                            {
                                case PayloadAction.Continue:
                                {
                                    continue;
                                }
                                default:
                                {
                                    return determineAction.Entity;
                                }
                            }
                        }
                    }
                }

                return PayloadAction.Reconnect;
            }
            catch (OperationCanceledException)
            {
                return PayloadAction.Disconnect;
            }
            catch (Exception e)
            {
                return e;
            }
        }

        /// <summary>
        /// This method acts as the main entrypoint for the gateway sender task. It processes payloads that are
        /// submitted by the application to the gateway, sending them to it.
        /// </summary>
        /// <param name="ct">A token for requests to disconnect the socket..</param>
        /// <returns>A sender result which may or may not have been successful. A failed result indicates that something
        /// has gone wrong when sending a payload, and that the connection has been deemed nonviable. A nonviable
        /// connection should be either terminated, reestablished, or resumed as appropriate.</returns>
        private async Task<Result> ClientEngineSenderAsync(CancellationToken ct = default)
        {
            await Task.Yield();

            try
            {
                DateTime? lastHeartbeat = null;
                while (!ct.IsCancellationRequested)
                {
                    var lastReceivedHeartbeatAck = Interlocked.Read(ref _lastReceivedHeartbeatAck);
                    var lastHeartbeatAck = lastReceivedHeartbeatAck > 0
                        ? DateTime.FromBinary(lastReceivedHeartbeatAck)
                        : (DateTime?)null;

                    // Heartbeat, if required
                    var now = DateTime.UtcNow;
                    if (lastHeartbeat is null || now - lastHeartbeat >= _heartbeatInterval)
                    {
                        if (lastHeartbeatAck < lastHeartbeat)
                        {
                            return new GatewayError
                            (
                                "The server did not respond in time with a heartbeat acknowledgement."
                            );
                        }

                        var heartbeatPayload = CreatePayload(CreateHeartbeat());
                        var sendHeartbeat = await _transportService.SendPayloadAsync(heartbeatPayload, ct);

                        if (!sendHeartbeat.IsSuccess)
                        {
                            return Result.FromError(new GatewayError("Failed to send a heartbeat."), sendHeartbeat);
                        }

                        lastHeartbeat = DateTime.UtcNow;
                    }

                    // Check if there are any user-submitted payloads to send
                    if (!_payloadsToSend.TryDequeue(out var payload))
                    {
                        // Let's sleep for a little while
                        var maxSleepTime = lastHeartbeat.Value + _heartbeatInterval - now;
                        var sleepTime = TimeSpan.FromMilliseconds(Math.Clamp(100, 0, maxSleepTime.TotalMilliseconds));

                        await Task.Delay(sleepTime, ct);
                        continue;
                    }

                    var sendResult = await _transportService.SendPayloadAsync(payload, ct);
                    if (sendResult.IsSuccess)
                    {
                        continue;
                    }

                    // Normal closures are okay
                    return sendResult.Error is GatewayWebSocketError { CloseStatus: NormalClosure }
                        ? Result.FromSuccess()
                        : sendResult;
                }

                return Result.FromSuccess();
            }
            catch (OperationCanceledException)
            {
                // Cancellation is a success
                return Result.FromSuccess();
            }
            catch (Exception ex)
            {
                return ex;
            }
        }
    }
}
