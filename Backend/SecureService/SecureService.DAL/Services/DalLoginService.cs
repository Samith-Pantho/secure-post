﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using SecureService.Context;
using SecureService.DAL.Repositories;
using SecureService.Entity.Shared.Database;
using SecureService.Entity.Shared.Internal;
using SecureService.Entity.Shared.Status;
using SecureService.Logging;

namespace SecureService.DAL.Services
{
    public class DalLoginService : IDalLoginRepository
    {
        private readonly IConfiguration _config;
        private readonly ILogRepository _logger;
        private readonly IDalClsRepository _cls;
        private readonly IDalJWTTokenRepository _jwt;
        private readonly SecureServiceDbContext _context;

        public DalLoginService(ILogRepository logger,
            IConfiguration config,
            IDalClsRepository cls,
            IDalJWTTokenRepository jwt,
            SecureServiceDbContext context)
        {
            this._logger = logger;
            this._config = config;
            this._cls = cls;
            this._jwt = jwt;
            this._context = context;
        }

        public StatusResult<object> Login(string encryptData)
        {
            StatusResult<object> status = new StatusResult<object>();
            UserDetail checkForExistingUserByUserID = new UserDetail();
            try
            {
                LoginViewModel loginModel = JsonConvert.DeserializeObject<LoginViewModel>(_cls.Decrypt(encryptData));
                checkForExistingUserByUserID = _context.UserDetail.Where(it => it.UserId.Equals(loginModel.UserId)).FirstOrDefault();

                if (checkForExistingUserByUserID != null)
                {
                    if (CryptographicOperations.FixedTimeEquals(
                        Encoding.UTF8.GetBytes(_cls.EncryptSha256Hash(loginModel.Password)),
                        Encoding.UTF8.GetBytes(checkForExistingUserByUserID.Password)))
                    {
                        if (checkForExistingUserByUserID.IsActiveFlag)
                        {
                            checkForExistingUserByUserID.FailedLoginAttemptNo = 0;
                            checkForExistingUserByUserID.LastLoginAt = DateTime.UtcNow;
                            checkForExistingUserByUserID.Isupdated = true;

                            status.Status = "OK";
                        }
                        else
                        {
                            throw new Exception("Account has been Deactivated. Please contact with Admiistrator.");
                        }
                    }
                    else
                    {
                        if (checkForExistingUserByUserID.FailedLoginAttemptNo == 3)
                        {
                            checkForExistingUserByUserID.IsActiveFlag = false;
                            checkForExistingUserByUserID.Isupdated = true;
                            throw new Exception("Account has been Deactivated, because of 3 times wrong password attempt.");
                        }
                        else
                        {
                            checkForExistingUserByUserID.FailedLoginAttemptNo += 1;
                            checkForExistingUserByUserID.Isupdated = true;
                            throw new Exception("Wrong Password.");
                        }
                    }

                    if (status.Status == "OK")
                    {
                        var usersSessions = _context.UserSession.Where(it => it.UserId == checkForExistingUserByUserID.UserId && it.IsActiveSessionFlag == true).ToList();
                        foreach (UserSession userSession in usersSessions)
                        {
                            userSession.IsActiveSessionFlag = false;
                            _context.UserSession.Update(userSession);
                            _context.SaveChanges();
                        }

                        UserSession usersNewSession = new UserSession();
                        usersNewSession.UserId = checkForExistingUserByUserID.UserId;
                        usersNewSession.SessionID = Guid.NewGuid().ToString();
                        usersNewSession.IsActiveSessionFlag = true;
                        usersNewSession.SessionStartTime = DateTime.UtcNow;
                        usersNewSession.SessionEndTime = DateTime.UtcNow.AddMinutes(15);
                        _context.UserSession.Add(usersNewSession);
                        if (_context.SaveChanges() > 0)
                        {
                            UserRefreshToken userNewRefreshToken = new UserRefreshToken();
                            userNewRefreshToken.UserId = checkForExistingUserByUserID.UserId;
                            userNewRefreshToken.TokenId = usersNewSession.SessionID + "#" + Guid.NewGuid().ToString();
                            userNewRefreshToken.IsActive = true;
                            userNewRefreshToken.CreatedAt = DateTime.UtcNow;
                            userNewRefreshToken.ExpiryDate = DateTime.UtcNow.AddDays(7);
                            _context.UserRefreshToken.Add(userNewRefreshToken);

                            if (_context.SaveChanges() > 0)
                            {
                                TokenResponseViewModel token = _jwt.GenerateToken(usersNewSession, userNewRefreshToken);
                                token.UserID = checkForExistingUserByUserID.UserId;
                                token.Email = checkForExistingUserByUserID.Email;
                                if (token != null)
                                {
                                    status.Status = "OK";
                                    status.Message = "Login Successfull.";
                                    status.Result = token;
                                }
                                else
                                {
                                    status.Status = "FAILED";
                                    status.Message = "Login Failed.";
                                }
                            }
                        }
                        else
                        {
                            status.Status = "FAILED";
                            status.Message = "Login Failed.";
                        }
                    }


                }
                else
                    throw new Exception("Invalid user.");
            }
            catch (Exception ex)
            {
                _logger.LogError("Error in Login : " + ex.Message + " , Stacktrace: " + ex.StackTrace);
                status.Status = "FAILED";
                status.Message = ex.Message;
                status.Result = null;
            }
            finally
            {
                if (checkForExistingUserByUserID != null && checkForExistingUserByUserID.Isupdated)
                {
                    _context.UserDetail.Update(checkForExistingUserByUserID);
                    _context.SaveChanges();
                }
            }
            return status;
        }
    }
}
