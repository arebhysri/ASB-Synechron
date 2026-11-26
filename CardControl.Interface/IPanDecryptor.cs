using System;
using System.Collections.Generic;
using System.Text;

namespace CardControl.Interface
{
    public interface IPanDecryptor
    {
        string Decrypt(string encryptedPan);
    }
}
