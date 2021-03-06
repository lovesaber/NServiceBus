﻿namespace NServiceBus
{
    using System;
    using System.IO;
    using System.Transactions;
    using NServiceBus.DataBus;
    using Pipeline;
    using Pipeline.Contexts;
    using Unicast.Transport;

    class DataBusSendBehavior : IBehavior<OutgoingContext>
    {
        public IDataBus DataBus { get; set; }

        public IDataBusSerializer DataBusSerializer { get; set; }

        public Conventions Conventions { get; set; }

        public void Invoke(OutgoingContext context, Action next)
        {
            if (context.OutgoingLogicalMessage.IsControlMessage())
            {
                next();
                return;
            }

            var timeToBeReceived = context.OutgoingLogicalMessage.Metadata.TimeToBeReceived;
            var message = context.OutgoingLogicalMessage.Instance;

            foreach (var property in Conventions.GetDataBusProperties(message))
            {
                var propertyValue = property.Getter(message);

                if (propertyValue == null)
                    continue;

                using (var stream = new MemoryStream())
                {
                    var dataBusProperty = propertyValue as IDataBusProperty;

                    if (dataBusProperty != null)
                    {
                        propertyValue = dataBusProperty.GetValue();
                    }

                    DataBusSerializer.Serialize(propertyValue, stream);
                    stream.Position = 0;

                    string headerValue;

                    using (new TransactionScope(TransactionScopeOption.Suppress))
                    {
                        headerValue = DataBus.Put(stream, timeToBeReceived);
                    }

                    string headerKey;

                    if (dataBusProperty != null)
                    {
                        dataBusProperty.Key = headerValue;
                        //we use the headers to in order to allow the infrastructure (eg. the gateway) to modify the actual key
                        headerKey = headerValue;
                    }
                    else
                    {
                        property.Setter(message, null);
                        headerKey = String.Format("{0}.{1}", message.GetType().FullName, property.Name);
                    }

                    //we use the headers to in order to allow the infrastructure (eg. the gateway) to modify the actual key
                    context.OutgoingLogicalMessage.Headers["NServiceBus.DataBus." + headerKey] = headerValue;
                }
            }

            next();
        }

        public class Registration : RegisterStep
        {
            public Registration(): base("DataBusSend", typeof(DataBusSendBehavior), "Saves the payload into the shared location")
            {
                InsertAfter(WellKnownStep.MutateOutgoingMessages);
                InsertBefore(WellKnownStep.CreatePhysicalMessage);
            }
        }
    }
}
