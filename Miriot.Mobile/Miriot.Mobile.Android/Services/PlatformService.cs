﻿using Miriot.Services;
using System;

namespace Miriot.Droid.Services
{
    public class PlatformService : IPlatformService
    {
        public bool IsInternetAvailable => true; //ConnectionHelper.IsInternetAvailable;

        public string GetSystemIdentifier()
        {
            throw new NotImplementedException();
        }
    }
}