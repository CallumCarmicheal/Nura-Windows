using System.Collections.Concurrent;
using System.Threading.Channels;

using NuraLib.Devices;
using NuraLib.Logging;
using NuraLib.Protocol;
using NuraLib.Transport;

internal sealed class TransportDispatcherTests {
    public async Task RunAllAsync() {
        LogFiltering_SuppressesDisabledLevels();
        await FragmentedFrame_CompletesExpectedExchangeAsync();
        await Indication_IsDeliveredWhileCommandIsPendingAsync();
        await UnexpectedResponse_DoesNotCompleteActiveExchangeAsync();
        await ConcurrentRequests_AreSerializedAsync();
        await PartialWrites_SendEntireFrameAsync();
        await ExtendedLengthFrame_CompletesExpectedExchangeAsync();
        await ReceiveFailure_RaisesDisconnectedAndFailsPendingExchangeAsync();
        await Disposal_FailsPendingExchangeAsync();
    }

    private static void LogFiltering_SuppressesDisabledLevels() {
        var emitted = new List<NuraLogEventArgs>();
        var logger = new NuraClientLogger(emitted.Add, () => NuraLogLevel.Information);
        logger.Trace("test", "trace");
        logger.Debug("test", "debug");
        logger.Information("test", "information");

        AssertEqual(1, emitted.Count, nameof(LogFiltering_SuppressesDisabledLevels));
        AssertEqual(NuraLogLevel.Information, emitted[0].Level, nameof(LogFiltering_SuppressesDisabledLevels));
    }

    private static async Task FragmentedFrame_CompletesExpectedExchangeAsync() {
        await using var fixture = await CreateFixtureAsync();
        var request = GaiaPacketFactory.CreateCommand(GaiaCommandId.EntryAppEncryptedAuthenticated);
        var exchange = fixture.Transport.ExchangeAsync(request, GaiaCommandId.ResponseAppEncryptedAuthenticated, CancellationToken.None);
        await fixture.Connection.WaitForSendAsync();

        var response = CreateResponseFrame(0x000a, [0x00, 0x02]);
        fixture.Connection.EnqueueIncoming(response[..3]);
        fixture.Connection.EnqueueIncoming(response[3..]);

        var actual = await exchange;
        AssertEqual((ushort)0x000a, actual.CommandId, nameof(FragmentedFrame_CompletesExpectedExchangeAsync));
        AssertEqual((byte)0x02, actual.PayloadExcludingStatus.Single(), nameof(FragmentedFrame_CompletesExpectedExchangeAsync));
    }

    private static async Task Indication_IsDeliveredWhileCommandIsPendingAsync() {
        await using var fixture = await CreateFixtureAsync();
        var indicationReceived = new TaskCompletionSource<GaiaResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Transport.IndicationReceived += response => indicationReceived.TrySetResult(response);

        var request = GaiaPacketFactory.CreateCommand(GaiaCommandId.EntryAppEncryptedAuthenticated);
        var exchange = fixture.Transport.ExchangeAsync(request, GaiaCommandId.ResponseAppEncryptedAuthenticated, CancellationToken.None);
        await fixture.Connection.WaitForSendAsync();

        var indication = CreateResponseFrame(
            (ushort)GaiaCommandId.IndicationFromHeadset,
            [0x00, (byte)HeadsetIndicationIdentifier.TouchButtonPressed, 0x03]);
        var response = CreateResponseFrame(0x000a, [0x00, 0x01]);
        fixture.Connection.EnqueueIncoming(indication.Concat(response).ToArray());

        var receivedIndication = await indicationReceived.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var receivedResponse = await exchange;
        AssertEqual((ushort)GaiaCommandId.IndicationFromHeadset, receivedIndication.CommandId, nameof(Indication_IsDeliveredWhileCommandIsPendingAsync));
        AssertEqual((ushort)0x000a, receivedResponse.CommandId, nameof(Indication_IsDeliveredWhileCommandIsPendingAsync));
    }

    private static async Task UnexpectedResponse_DoesNotCompleteActiveExchangeAsync() {
        await using var fixture = await CreateFixtureAsync();
        var request = GaiaPacketFactory.CreateCommand(GaiaCommandId.EntryAppEncryptedAuthenticated);
        var exchange = fixture.Transport.ExchangeAsync(request, GaiaCommandId.ResponseAppEncryptedAuthenticated, CancellationToken.None);
        await fixture.Connection.WaitForSendAsync();

        fixture.Connection.EnqueueIncoming(CreateResponseFrame(0x000b, [0x00, 0x55]));
        await Task.Delay(50);
        AssertTrue(!exchange.IsCompleted, nameof(UnexpectedResponse_DoesNotCompleteActiveExchangeAsync), "Unexpected response completed the active exchange.");

        fixture.Connection.EnqueueIncoming(CreateResponseFrame(0x000a, [0x00, 0x02]));
        var actual = await exchange;
        AssertEqual((ushort)0x000a, actual.CommandId, nameof(UnexpectedResponse_DoesNotCompleteActiveExchangeAsync));
    }

    private static async Task ConcurrentRequests_AreSerializedAsync() {
        await using var fixture = await CreateFixtureAsync();
        var first = fixture.Transport.ExchangeAsync(
            GaiaPacketFactory.CreateCommand(GaiaCommandId.EntryAppEncryptedAuthenticated),
            GaiaCommandId.ResponseAppEncryptedAuthenticated,
            CancellationToken.None);
        await fixture.Connection.WaitForSendAsync();

        var second = fixture.Transport.ExchangeAsync(
            GaiaPacketFactory.CreateCommand(GaiaCommandId.EntryAppEncryptedAuthenticated),
            GaiaCommandId.ResponseAppEncryptedAuthenticated,
            CancellationToken.None);
        await Task.Delay(50);
        AssertEqual(1, fixture.Connection.SendCount, nameof(ConcurrentRequests_AreSerializedAsync));

        fixture.Connection.EnqueueIncoming(CreateResponseFrame(0x000a, [0x00, 0x01]));
        await first;
        await fixture.Connection.WaitForSendAsync();
        AssertEqual(2, fixture.Connection.SendCount, nameof(ConcurrentRequests_AreSerializedAsync));

        fixture.Connection.EnqueueIncoming(CreateResponseFrame(0x000a, [0x00, 0x02]));
        await second;
    }

    private static async Task PartialWrites_SendEntireFrameAsync() {
        await using var fixture = await CreateFixtureAsync(maximumWriteSize: 3);
        var request = GaiaPacketFactory.CreateCommand(GaiaCommandId.EntryAppEncryptedAuthenticated);
        var exchange = fixture.Transport.ExchangeAsync(request, GaiaCommandId.ResponseAppEncryptedAuthenticated, CancellationToken.None);
        await fixture.Connection.WaitForSendAsync();

        fixture.Connection.EnqueueIncoming(CreateResponseFrame(0x000a, [0x00, 0x00]));
        await exchange;
        AssertSequenceEqual(request.Bytes, fixture.Connection.SentBytes, nameof(PartialWrites_SendEntireFrameAsync));
    }

    private static async Task ExtendedLengthFrame_CompletesExpectedExchangeAsync() {
        await using var fixture = await CreateFixtureAsync();
        var request = GaiaPacketFactory.CreateCommand(GaiaCommandId.EntryAppEncryptedAuthenticated);
        var exchange = fixture.Transport.ExchangeAsync(request, GaiaCommandId.ResponseAppEncryptedAuthenticated, CancellationToken.None);
        await fixture.Connection.WaitForSendAsync();

        fixture.Connection.EnqueueIncoming(CreateResponseFrame(0x000a, [0x00, 0x03], flags: 0x02));
        var response = await exchange;
        AssertEqual((ushort)0x000a, response.CommandId, nameof(ExtendedLengthFrame_CompletesExpectedExchangeAsync));
        AssertEqual((byte)0x03, response.PayloadExcludingStatus.Single(), nameof(ExtendedLengthFrame_CompletesExpectedExchangeAsync));
    }

    private static async Task ReceiveFailure_RaisesDisconnectedAndFailsPendingExchangeAsync() {
        await using var fixture = await CreateFixtureAsync();
        var disconnected = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Transport.Disconnected += exception => disconnected.TrySetResult(exception);

        var exchange = fixture.Transport.ExchangeAsync(
            GaiaPacketFactory.CreateCommand(GaiaCommandId.EntryAppEncryptedAuthenticated),
            GaiaCommandId.ResponseAppEncryptedAuthenticated,
            CancellationToken.None);
        await fixture.Connection.WaitForSendAsync();
        fixture.Connection.CompleteIncoming();

        await AssertThrowsAsync<ChannelClosedException>(exchange, nameof(ReceiveFailure_RaisesDisconnectedAndFailsPendingExchangeAsync));
        var exception = await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(1));
        AssertTrue(exception is ChannelClosedException, nameof(ReceiveFailure_RaisesDisconnectedAndFailsPendingExchangeAsync), "Unexpected disconnect exception.");
    }

    private static async Task Disposal_FailsPendingExchangeAsync() {
        await using var fixture = await CreateFixtureAsync();
        var exchange = fixture.Transport.ExchangeAsync(
            GaiaPacketFactory.CreateCommand(GaiaCommandId.EntryAppEncryptedAuthenticated),
            GaiaCommandId.ResponseAppEncryptedAuthenticated,
            CancellationToken.None);
        await fixture.Connection.WaitForSendAsync();

        await fixture.Transport.DisposeAsync();
        await AssertThrowsAsync<ObjectDisposedException>(exchange, nameof(Disposal_FailsPendingExchangeAsync));
    }

    private static async Task<TransportFixture> CreateFixtureAsync(int maximumWriteSize = int.MaxValue) {
        var connection = new ScriptedRfcommDuplexConnection(maximumWriteSize);
        var logger = new NuraClientLogger(_ => { });
        var transport = new RfcommHeadsetTransport(logger, () => connection);
        await transport.ConnectAsync("00:11:22:33:44:55", CancellationToken.None);
        return new TransportFixture(transport, connection);
    }

    private static byte[] CreateResponseFrame(ushort commandId, byte[] payload, byte flags = 0x00) {
        return GaiaPacketFactory.CreateRawCommand((ushort)(0x8000 | commandId), payload, flags: flags).Bytes;
    }

    private static async Task AssertThrowsAsync<TException>(Task task, string testName) where TException : Exception {
        try {
            await task;
        } catch (TException) {
            return;
        }

        throw new InvalidOperationException($"{testName}: expected {typeof(TException).Name}.");
    }

    private static void AssertEqual<T>(T expected, T actual, string testName) {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) {
            throw new InvalidOperationException($"{testName}: expected '{expected}' but got '{actual}'.");
        }
    }

    private static void AssertTrue(bool condition, string testName, string message) {
        if (!condition) {
            throw new InvalidOperationException($"{testName}: {message}");
        }
    }

    private static void AssertSequenceEqual(byte[] expected, byte[] actual, string testName) {
        if (!expected.SequenceEqual(actual)) {
            throw new InvalidOperationException($"{testName}: sent bytes did not match the complete request frame.");
        }
    }

    private sealed class TransportFixture : IAsyncDisposable {
        public TransportFixture(RfcommHeadsetTransport transport, ScriptedRfcommDuplexConnection connection) {
            Transport = transport;
            Connection = connection;
        }

        public RfcommHeadsetTransport Transport { get; }

        public ScriptedRfcommDuplexConnection Connection { get; }

        public ValueTask DisposeAsync() => Transport.DisposeAsync();
    }

    private sealed class ScriptedRfcommDuplexConnection : IRfcommDuplexConnection {
        private readonly Channel<byte[]> _incoming = Channel.CreateUnbounded<byte[]>();
        private readonly Channel<byte> _sendNotifications = Channel.CreateUnbounded<byte>();
        private readonly ConcurrentQueue<byte> _sentBytes = new();
        private readonly int _maximumWriteSize;
        private byte[]? _currentRead;
        private int _currentReadOffset;
        private int _sendCount;

        public ScriptedRfcommDuplexConnection(int maximumWriteSize) {
            _maximumWriteSize = maximumWriteSize;
        }

        public int SendCount => Volatile.Read(ref _sendCount);

        public byte[] SentBytes => _sentBytes.ToArray();

        public Task ConnectAsync(string deviceAddress, CancellationToken cancellationToken) => Task.CompletedTask;

        public async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken) {
            while (_currentRead is null || _currentReadOffset >= _currentRead.Length) {
                _currentRead = await _incoming.Reader.ReadAsync(cancellationToken);
                _currentReadOffset = 0;
            }

            var length = Math.Min(buffer.Length, _currentRead.Length - _currentReadOffset);
            _currentRead.AsSpan(_currentReadOffset, length).CopyTo(buffer.Span);
            _currentReadOffset += length;
            return length;
        }

        public ValueTask<int> SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken) {
            var length = Math.Min(buffer.Length, _maximumWriteSize);
            foreach (var value in buffer.Span[..length]) {
                _sentBytes.Enqueue(value);
            }

            Interlocked.Increment(ref _sendCount);
            _sendNotifications.Writer.TryWrite(0);
            return ValueTask.FromResult(length);
        }

        public ValueTask DisposeAsync() {
            _incoming.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }

        public void EnqueueIncoming(byte[] bytes) {
            if (!_incoming.Writer.TryWrite(bytes)) {
                throw new InvalidOperationException("The scripted connection is closed.");
            }
        }

        public void CompleteIncoming() {
            _incoming.Writer.TryComplete();
        }

        public async Task WaitForSendAsync() {
            await _sendNotifications.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));
        }
    }
}
