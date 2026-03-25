using System.Threading.Channels;

namespace Logs2Obs.Worker.Tests.Pipeline;

public class ChannelBackpressureTests
{
    [Fact]
    public async Task BoundedChannel_WhenFull_WaitsForConsumer()
    {
        var channel = Channel.CreateBounded<int>(new BoundedChannelOptions(2)
        {
            FullMode = BoundedChannelFullMode.Wait
        });

        // Fill the channel
        await channel.Writer.WriteAsync(1);
        await channel.Writer.WriteAsync(2);

        // Next write should block until reader consumes
        var writeTask = channel.Writer.WriteAsync(3).AsTask();
        await Task.Delay(50); // Give time for async machinery
        writeTask.IsCompleted.Should().BeFalse("channel should be full and write should block");

        // Consume one item
        var item = await channel.Reader.ReadAsync();
        item.Should().Be(1);

        // Now write should complete
        await writeTask;
        writeTask.IsCompletedSuccessfully.Should().BeTrue("write should complete after reader consumed one item");
    }

    [Fact]
    public async Task BoundedChannel_WhenConsumerDrains_AllowsNewWrites()
    {
        var channel = Channel.CreateBounded<int>(new BoundedChannelOptions(3)
        {
            FullMode = BoundedChannelFullMode.Wait
        });

        // Write 3 items (fills channel)
        await channel.Writer.WriteAsync(1);
        await channel.Writer.WriteAsync(2);
        await channel.Writer.WriteAsync(3);

        // Consumer drains all
        var items = new List<int>();
        while (await channel.Reader.WaitToReadAsync())
        {
            if (channel.Reader.TryRead(out var item))
            {
                items.Add(item);
            }

            if (items.Count == 3)
                break;
        }

        items.Should().BeEquivalentTo([1, 2, 3]);

        // Channel should now accept new writes
        await channel.Writer.WriteAsync(4);
        await channel.Writer.WriteAsync(5);
        await channel.Writer.WriteAsync(6);

        var newItem = await channel.Reader.ReadAsync();
        newItem.Should().Be(4);
    }

    [Fact]
    public async Task BoundedChannel_WithWaitMode_DoesNotDropMessages()
    {
        var channel = Channel.CreateBounded<int>(new BoundedChannelOptions(2)
        {
            FullMode = BoundedChannelFullMode.Wait
        });

        var writeTask = Task.Run(async () =>
        {
            for (int i = 1; i <= 10; i++)
            {
                await channel.Writer.WriteAsync(i);
            }
            channel.Writer.Complete();
        });

        var readTask = Task.Run(async () =>
        {
            var items = new List<int>();
            await foreach (var item in channel.Reader.ReadAllAsync())
            {
                items.Add(item);
                await Task.Delay(10); // Simulate slow consumer
            }
            return items;
        });

        await writeTask;
        var result = await readTask;

        result.Should().HaveCount(10, "all messages should be delivered despite slow consumer");
        result.Should().BeInAscendingOrder("messages should arrive in order");
    }

    [Fact]
    public async Task UnboundedChannel_NeverBlocks_CanOverflow()
    {
        var channel = Channel.CreateUnbounded<int>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        // Write many items without blocking
        for (int i = 1; i <= 1000; i++)
        {
            await channel.Writer.WriteAsync(i);
        }

        // Count items in channel (note: ChannelReader.Count is not supported, so we read all)
        int count = 0;
        while (channel.Reader.TryRead(out _))
        {
            count++;
        }

        count.Should().Be(1000, "unbounded channel accepts all writes");

        // This demonstrates why we DON'T use unbounded channels in production
        // (memory can grow unbounded if consumer is slower than producer)
    }
}
