// <copyright>
// Copyright 2021 Max Ieremenko
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//  http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// </copyright>

using MemoryPack;
using ServiceModel.Grpc.Channel;
using ServiceModel.Grpc.MemoryPackaArshaller;

namespace ServiceModel.Grpc.Configuration;

internal sealed class MessageMemoryPackFormatter : MemoryPackFormatter<Message>
{
    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref Message? value)
    {
        ArgumentNullException.ThrowIfNull(value);
        writer.WritePackable(new SerializableMessage());
    }

    public override void Deserialize(ref MemoryPackReader reader, scoped ref Message? value)
    {
        if (reader.PeekIsNull())
        {
            // throw new NotSupportedException();
            value = null;
            return;
        }

        reader.ReadPackable<SerializableMessage>();
        value = new();
    }
}

internal sealed class MessageMemoryPackFormatter<T> : MemoryPackFormatter<Message<T>>
{
    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref Message<T>? value)
    {
        ArgumentNullException.ThrowIfNull(value);
        writer.WritePackable(new SerializableMessage<T>(value));
    }

    public override void Deserialize(ref MemoryPackReader reader, scoped ref Message<T>? value)
    {
        if (reader.PeekIsNull())
        {
            value = null;
            return;
        }

        var wrapped = reader.ReadPackable<SerializableMessage<T>>();
        value = wrapped.Message;
    }
}

internal sealed class MessageMemoryPackFormatter<T1, T2> : MemoryPackFormatter<Message<T1, T2>>
{
    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref Message<T1, T2>? value)
    {
        ArgumentNullException.ThrowIfNull(value);
        writer.WritePackable(new SerializableMessage<T1, T2>(value));
    }

    public override void Deserialize(ref MemoryPackReader reader, scoped ref Message<T1, T2>? value)
    {
        if (reader.PeekIsNull())
        {
            value = null;
            return;
        }

        var wrapped = reader.ReadPackable<SerializableMessage<T1, T2>>();
        value = wrapped.Message;
    }
}

internal sealed class MessageMemoryPackFormatter<T1, T2, T3> : MemoryPackFormatter<Message<T1, T2, T3>>
{
    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref Message<T1, T2, T3>? value)
    {
        ArgumentNullException.ThrowIfNull(value);
        writer.WritePackable(new SerializableMessage<T1, T2, T3>(value));
    }

    public override void Deserialize(ref MemoryPackReader reader, scoped ref Message<T1, T2, T3>? value)
    {
        if (reader.PeekIsNull())
        {
            value = null;
            return;
        }

        var wrapped = reader.ReadPackable<SerializableMessage<T1, T2, T3>>();
        value = wrapped.Message;
    }
}
