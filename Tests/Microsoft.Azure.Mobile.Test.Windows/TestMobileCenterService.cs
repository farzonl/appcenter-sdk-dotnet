﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.Mobile.Channel;
using Microsoft.Azure.Mobile.Utils;

namespace Microsoft.Azure.Mobile.Test.Windows
{
    public class TestMobileCenterService : MobileCenterService
    {
        public TestMobileCenterService(IApplicationSettings settings) : base(settings)
        {
        }

        public TestMobileCenterService()
        {
        }

        protected override string ChannelName => "test_service";
        protected override string ServiceName => "TestService";

        public IChannelGroup PublicChannelGroup => ChannelGroup;
        public IChannel PublicChannel => Channel;
        public string PublicEnabledPreferenceKey => EnabledPreferenceKey;
        public bool PublicIsInactive => IsInactive;
        public string PublicChannelName => ChannelName;
    }
}
