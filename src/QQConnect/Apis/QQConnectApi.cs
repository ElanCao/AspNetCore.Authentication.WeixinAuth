﻿using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.Net.Http;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Http;
using System.Net.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace AspNetCore.Authentication.QQConnect
{
    public class QQConnectApi : IQQConnectApi
    {
        public ILogger Logger { get; }
        QQConnectOptions Options;

        public QQConnectApi(
            IOptionsMonitor<QQConnectOptions> optionsAccessor,
            ILoggerFactory loggerFactory)
        {
            Options = optionsAccessor?.CurrentValue ?? throw new ArgumentNullException(nameof(optionsAccessor));
            Logger = loggerFactory?.CreateLogger<QQConnectApi>() ?? throw new ArgumentNullException(nameof(loggerFactory));
        }

        private static async Task<string> Display(HttpResponseMessage response)
        {
            var output = new StringBuilder();
            output.Append("Status: " + response.StatusCode + ";");
            output.Append("Headers: " + response.Headers.ToString() + ";");
            output.Append("Body: " + await response.Content.ReadAsStringAsync() + ";");
            return output.ToString();
        }

        /// <summary>
        /// 换取access_token
        /// </summary>
        public async Task<OAuthTokenResponse> GetToken(HttpClient backchannel, string tokenEndpoint, string clientId, string clientSecret, string code, string redirectUri, CancellationToken cancellationToken)
        {
            var tokenRequestParameters = new Dictionary<string, string>()
            {
                { "grant_type", "authorization_code" },
                { "client_id", clientId },
                { "client_secret", clientSecret },
                { "code", code },
                { "redirect_uri", redirectUri },
            };

            var requestUrl = QueryHelpers.AddQueryString(tokenEndpoint, tokenRequestParameters);

            var response = await backchannel.GetAsync(requestUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = "OAuth token endpoint failure: " + await Display(response);
                Logger.LogError(error);
                return OAuthTokenResponse.Failed(new Exception(error));
            }

            var content = await response.Content.ReadAsStringAsync();
            //callback( {"error":100001,"error_description":"param client_id is wrong or lost "} );
            //callback( {"error":100010,"error_description":"redirect uri is illegal"} );
            //access_token=FE04************************CCE2&expires_in=7776000&refresh_token=88E4************************BE14
            var payload = ParseCallbackString(content);
            if (!IsCallbackSuccess(payload))
            {
                var error = $"OAuth openid endpoint failure: " + await Display(response);
                Logger.LogError(error);
                throw new HttpRequestException(error);
            }
            return OAuthTokenResponse.Success(payload);
        }

        private static JObject ParseQuery(string query)
        {
            var jObject = new JObject();

            foreach (var kv in query.Split('&'))
            {
                var keyValue = kv.Split('=');

                jObject.Add(keyValue[0], new JValue(keyValue[1]));
            }

            return jObject;
        }

        /// <summary>
        /// 刷新或续期access_token使用。由于access_token有效期（目前为2个小时）较短，当access_token超时后，可以使用refresh_token进行刷新。
        /// </summary>
        /// <param name="refreshToken">refresh_token拥有较长的有效期（30天），当refresh_token失效的后，需要用户重新授权，所以，请开发者在refresh_token即将过期时（如第29天时），进行定时的自动刷新并保存好它。</param>
        /// <returns></returns>
        public async Task<OAuthTokenResponse> RefreshToken(HttpClient backchannel, string refreshTokenEndpoint, string appId, string refreshToken, CancellationToken cancellationToken)
        {
            await Task.FromResult<OAuthTokenResponse>(null);
            throw new NotImplementedException();
        }

        /// <summary>
        /// 检验授权凭证（access_token）是否有效。
        /// </summary>
        /// <param name="refreshToken"></param>
        /// <returns></returns>
        public async Task<bool> ValidateToken(HttpClient backchannel, string validateTokenEndpoint, string appId, string accessToken, CancellationToken cancellationToken)
        {
            await Task.FromResult<OAuthTokenResponse>(null);
            throw new NotImplementedException();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="backchannel"></param>
        /// <param name="endpoint">OpenIdEndpoint</param>
        /// <param name="accessToken"></param>
        /// <param name="openid"></param>
        /// <param name="languageCode"></param>
        /// <returns>{“client_id”:”YOUR_APPID”,”openid”:”YOUR_OPENID”}</returns>
        public async Task<JObject> GetOpenId(HttpClient backchannel, string endpoint, string accessToken, CancellationToken cancellationToken)
        {
            // Get the openId and clientId
            var openIdParameters = new Dictionary<string, string>()
            {
                { "access_token", accessToken}
            };
            var requestUrl = QueryHelpers.AddQueryString(Options.OpenIdEndpoint, openIdParameters);
            var response = await backchannel.GetAsync(requestUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = $"OAuth openid endpoint failure: " + Display(response);
                Logger.LogError(error);
                throw new HttpRequestException(error);
            }
            var content = await response.Content.ReadAsStringAsync();
            var payload = ParseCallbackString(content);
            //{“client_id”:”YOUR_APPID”,”openid”:”YOUR_OPENID”}
            if (!IsCallbackSuccess(payload))
            {
                var error = $"OAuth openid endpoint failure: " + Display(response);
                Logger.LogError(error);
                throw new HttpRequestException(error);
            }
            return payload;
        }

        private JObject ParseCallbackString(string content)
        {
            var contentRegex = new Regex(@"callback\((.*)\);", RegexOptions.Compiled);
            var match = contentRegex.Match(content);
            if (!match.Success)
            {
                var error = $"Failed on parsing the callback string: {content}";
                Logger.LogError(error);
                throw new HttpRequestException(error);
            }
            var json = match.Groups[1].Value;
            return JObject.Parse(json);
        }

        private bool IsCallbackSuccess(JObject payload)
        {
            //{ "ret":1002, "msg":"请先登录" }
            return payload.Value<int?>("ret").GetValueOrDefault(0) == 0;
        }

        /// <summary>
        /// 获取登录用户的昵称、头像、性别（get_user_info, UnionID机制）
        /// </summary>
        /// <param name="accessToken"></param>
        /// <param name="clientId">oauth_consumer_key</param>
        /// <param name="openid"></param>
        /// <returns></returns>
        public async Task<JObject> GetUserInfo(HttpClient backchannel, string userInformationEndpoint, string accessToken, string openid, string clientId, CancellationToken cancellationToken)
        {
            // Get the UserInfo
            var getUserInfoParameters = new Dictionary<string, string>()
            {
                {"access_token", accessToken},
                {"oauth_consumer_key", clientId},
                {"openid", openid }
            };

            var requestUrl = QueryHelpers.AddQueryString(userInformationEndpoint, getUserInfoParameters);

            var response = await backchannel.GetAsync(requestUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = $"OAuth userinformation endpoint failure: " + Display(response);
                Logger.LogError(error);
                throw new HttpRequestException(error);
            }

            var content = await response.Content.ReadAsStringAsync();
            //{
            //    "ret":0,
            //    "msg":"",
            //    "nickname":"Peter",
            //    "figureurl":"http://qzapp.qlogo.cn/qzapp/111111/942FEA70050EEAFBD4DCE2C1FC775E56/30",
            //    "figureurl_1":"http://qzapp.qlogo.cn/qzapp/111111/942FEA70050EEAFBD4DCE2C1FC775E56/50",
            //    "figureurl_2":"http://qzapp.qlogo.cn/qzapp/111111/942FEA70050EEAFBD4DCE2C1FC775E56/100",
            //    "figureurl_qq_1":"http://q.qlogo.cn/qqapp/100312990/DE1931D5330620DBD07FB4A5422917B6/40",
            //    "figureurl_qq_2":"http://q.qlogo.cn/qqapp/100312990/DE1931D5330620DBD07FB4A5422917B6/100",
            //    "gender":"男",
            //    "is_yellow_vip":"1",
            //    "vip":"1",
            //    "yellow_vip_level":"7",
            //    "level":"7",
            //    "is_yellow_year_vip":"1"
            //}
            // or
            //{ "ret":1002, "msg":"请先登录" }
            var payload = ParseCallbackString(content);
            if (!IsCallbackSuccess(payload))
            {
                var error = $"OAuth userinformation endpoint failure: " + await Display(response);
                Logger.LogError(error);
                throw new HttpRequestException(error);
            }
            return payload;
        }

        /// <summary>
        /// 访问用户QQ会员信息：获取QQ会员的基本信息(get_vip_info)
        /// </summary>
        /// <param name="backchannel"></param>
        /// <param name="userInformationEndpoint"></param>
        /// <param name="accessToken"></param>
        /// <param name="openid"></param>
        /// <param name="clientId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<JObject> GetUserVipInfo(HttpClient backchannel, string userInformationEndpoint, string accessToken, string openid, string clientId, CancellationToken cancellationToken)
        {
            await Task.FromResult<JObject>(null);
            throw new NotImplementedException();
        }

        /// <summary>
        /// 访问用户QQ会员信息：获取QQ会员的高级信息(get_vip_rich_info)
        /// </summary>
        /// <param name="backchannel"></param>
        /// <param name="userInformationEndpoint"></param>
        /// <param name="accessToken"></param>
        /// <param name="openid"></param>
        /// <param name="clientId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<JObject> GetUserVipRichInfo(HttpClient backchannel, string userInformationEndpoint, string accessToken, string openid, string clientId, CancellationToken cancellationToken)
        {
            await Task.FromResult<JObject>(null);
            throw new NotImplementedException();
        }

        /// <summary>
        /// 访问我的空间相册：获取用户QQ空间相册列表(list_album)
        /// </summary>
        /// <param name="backchannel"></param>
        /// <param name="userInformationEndpoint"></param>
        /// <param name="accessToken"></param>
        /// <param name="openid"></param>
        /// <param name="clientId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<JObject> ListAlbum(HttpClient backchannel, string userInformationEndpoint, string accessToken, string openid, string clientId, CancellationToken cancellationToken)
        {
            await Task.FromResult<JObject>(null);
            throw new NotImplementedException();
        }

        /// <summary>
        /// 访问我的空间相册：上传一张照片到QQ空间相册(upload_pic)
        /// </summary>
        /// <param name="backchannel"></param>
        /// <param name="userInformationEndpoint"></param>
        /// <param name="accessToken"></param>
        /// <param name="openid"></param>
        /// <param name="clientId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<JObject> UploadPicture(HttpClient backchannel, string userInformationEndpoint, string accessToken, string openid, string clientId, CancellationToken cancellationToken)
        {
            await Task.FromResult<JObject>(null);
            throw new NotImplementedException();
        }

        /// <summary>
        /// 访问我的空间相册：在用户的空间相册里，创建一个新的个人相册(add_album)
        /// </summary>
        /// <param name="backchannel"></param>
        /// <param name="userInformationEndpoint"></param>
        /// <param name="accessToken"></param>
        /// <param name="openid"></param>
        /// <param name="clientId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<JObject> AddAlbum(HttpClient backchannel, string userInformationEndpoint, string accessToken, string openid, string clientId, CancellationToken cancellationToken)
        {
            await Task.FromResult<JObject>(null);
            throw new NotImplementedException();
        }

        /// <summary>
        /// 访问我的空间相册：获取用户QQ空间相册中的照片列表（list_photo)
        /// </summary>
        /// <param name="backchannel"></param>
        /// <param name="userInformationEndpoint"></param>
        /// <param name="accessToken"></param>
        /// <param name="openid"></param>
        /// <param name="clientId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<JObject> ListPhoto(HttpClient backchannel, string userInformationEndpoint, string accessToken, string openid, string clientId, CancellationToken cancellationToken)
        {
            await Task.FromResult<JObject>(null);
            throw new NotImplementedException();
        }
    }
}
