using AspNetCore.QcloudSms;
using System.Threading.Tasks;

namespace AspNetCore.QcloudSms
{
    internal static class QcloudSmsSenderExtensions
    {
        /// <summary>
        /// ������֤�����
        /// </summary>
        /// <param name="_smsSender"></param>
        /// <param name="vcode"></param>
        public static async Task<bool> SendVcodeAsync(this ISmsSender _smsSender, string mobile, string vcode)
        {
            var codeText = $"���¹����뻧��{vcode}Ϊ������֤�롣��Ǳ��˲���������Ա����š�";
            return await _smsSender.SendSmsAsync(mobile, codeText);
        }
    }
}
