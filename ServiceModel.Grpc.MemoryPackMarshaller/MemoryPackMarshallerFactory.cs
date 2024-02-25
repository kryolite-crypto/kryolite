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

using Grpc.Core;
using MemoryPack;
using MemoryPack.Formatters;
using ServiceModel.Grpc.Channel;
using ServiceModel.Grpc.MemoryPackaArshaller;

namespace ServiceModel.Grpc.Configuration;

public sealed class MemoryPackMarshallerFactory : IMarshallerFactory
{
    public static readonly IMarshallerFactory Default = new MemoryPackMarshallerFactory();

    static MemoryPackMarshallerFactory()
    {
        MemoryPackFormatterProvider.Register(new MessageMemoryPackFormatter());
        MemoryPackFormatterProvider.RegisterGenericType(typeof(Message<>), typeof(MessageMemoryPackFormatter<>));
        MemoryPackFormatterProvider.RegisterGenericType(typeof(Message<,>), typeof(MessageMemoryPackFormatter<,>));
        MemoryPackFormatterProvider.RegisterGenericType(typeof(Message<,,>), typeof(MessageMemoryPackFormatter<,,>));
    }

    public Marshaller<T> CreateMarshaller<T>() => new(Serialize, Deserialize<T>);

    private static void Serialize<T>(T value, SerializationContext context)
    {
        var bufferWriter = context.GetBufferWriter();
        MemoryPackSerializer.Serialize(bufferWriter, value);
        context.Complete();
    }

    private static T Deserialize<T>(DeserializationContext context)
    {
        return MemoryPackSerializer.Deserialize<T>(context.PayloadAsReadOnlySequence())!;
    }
}
