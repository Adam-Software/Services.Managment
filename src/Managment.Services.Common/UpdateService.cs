﻿using Managment.Interface;
using Managment.Interface.AppSettingsOptionsServiceDependency;
using Managment.Interface.CheckingUpdateServiceDependency;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Octokit;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;


namespace Managment.Services.Common
{
    public class UpdateService : IUpdateService
    {
        #region Services

        private readonly ILogger<UpdateService> mLogger;

        #endregion

        #region Var

        private readonly GitHubClient mGitHubClient;
        private readonly JsonRepository mJsonRepository;
        private readonly List<ServiceRepositoryModel> mSettingsServiceRepositories;
        private readonly List<ServiceRepositoryModel> mServiceRepositories;
        private readonly string mDownloadInfoFilesNamePath;
        private readonly string mDownloadRepositoriesFilesNamePath; 

        #endregion

        #region Events

        public event DownloadAndCheckUpdateStartedEventHandler RaiseDownloadAndCheckUpdateStartedEvent;
        public event DownloadAndCheckUpdateFinishedEventHandler RaiseDownloadAndCheckUpdateFinishedEvent;

        #endregion

        #region ~

        public UpdateService(IServiceProvider serviceProvider) 
        {
            mLogger = serviceProvider.GetRequiredService<ILogger<UpdateService>>();
            IAppSettingsOptionsService appSettingsOptionsService = serviceProvider.GetRequiredService<IAppSettingsOptionsService>();

            IGitHubCilentService gitHubClientService = serviceProvider.GetRequiredService<IGitHubCilentService>();
            mGitHubClient = gitHubClientService.GitHubClient;

            string servicesRepositoriesInfoDownloadPath = appSettingsOptionsService.UpdateServiceSettings.ServicesRepositoriesInfoDownloadPath;
            mJsonRepository = new(servicesRepositoriesInfoDownloadPath);
            
            mSettingsServiceRepositories = new List<ServiceRepositoryModel>(appSettingsOptionsService.UpdateServiceSettings.ServicesRepositories);
            mServiceRepositories = [];

            mDownloadInfoFilesNamePath = appSettingsOptionsService.UpdateServiceSettings.DownloadInfoFilesNamePath;
            mDownloadRepositoriesFilesNamePath = appSettingsOptionsService.UpdateServiceSettings.DownloadRepositoriesFilesNamePath;

            mLogger.LogInformation("=== UpdateService. Start ===");
        }

        #endregion

        #region Public methods

        public async Task DownloadAndCheckUpdateInfoFiles()
        {
            OnRaiseDownloadAndCheckUpdateStartedEvent();

            mJsonRepository.CreateOrClearRepositoryDirectory();

            await DownloadRepositoriesListAsync();
            await CheckAndSaveRepositoriesListAsync();
            await DownloadRepositoriesInfoAsync();
            //await DownloadFromSourceAsync();

            OnRaiseDownloadAndCheckUpdateFinishedEvent();
        }

        #endregion

        #region Private methods

        private async Task DownloadRepositoriesListAsync()
        {
            mLogger.LogInformation("=== Step 1. Download Repositories List ===");
            
            int i = 0;
            List<ServiceRepositoryModel> tempServiceRepositories = new(mSettingsServiceRepositories);

            foreach (ServiceRepositoryModel serviceRepository in tempServiceRepositories)
            {
                i++;
                bool savedWithException = false;
                mLogger.LogInformation("{counter}. Download repositories list from RepositoriesName:{RepositoriesName} RepositoriesOwner:{RepositoriesOwner} FileName:{ServicesListFilePath}", i, serviceRepository.RepositoriesName, serviceRepository.RepositoriesOwner, serviceRepository.ServicesListFilePath);
                string fileName = $"{serviceRepository.RepositoriesOwner.ToLower()}.{serviceRepository.RepositoriesName.ToLower()}.repositories.json";
     
                try
                {
                    byte[] fileContent = await mGitHubClient.Repository.Content.GetRawContent(serviceRepository.RepositoriesOwner, serviceRepository.RepositoriesName, serviceRepository.ServicesListFilePath);
                    string serviceRepositoriesList = System.Text.Encoding.Default.GetString(fileContent);    
                    await mJsonRepository.SaveRawJsonFilesAsync(serviceRepositoriesList, fileName);
                }
                catch (NotFoundException) 
                {
                    
                    mLogger.LogError("{counter}. The file or repository not found and removed from repositories list", i);
                    mSettingsServiceRepositories.Remove(serviceRepository);

                    savedWithException = true;
                }
                catch (Exception ex)
                {
                    mLogger.LogError("{counter}. {exception}", i, ex);
                    mSettingsServiceRepositories.Remove(serviceRepository);

                    savedWithException = true;
                }
                finally
                {
                    if (!savedWithException)
                        mLogger.LogInformation("{counter}. {filePath} saved!", i, $"{mJsonRepository.InfoDownloadPath}{Path.DirectorySeparatorChar}{fileName}");
                }   
            }

            mLogger.LogInformation("=== Step 1. Download Repositories Finished ===");
        }

        private async Task CheckAndSaveRepositoriesListAsync()
        {
            mLogger.LogInformation("=== Step 2. Check Repositories Info Files ===");

            List<string> repositoryInfoFilesName = [];

            foreach (ServiceRepositoryModel serviceRepository in mSettingsServiceRepositories)
            {
                string fileName = $"{serviceRepository.RepositoriesOwner.ToLower()}.{serviceRepository.RepositoriesName.ToLower()}.repositories.json";
                
                List<ServiceRepositoryModel> repositories = [];
                bool readWithException = false;

                try
                {
                    repositories = await mJsonRepository.ReadJsonFileAsync<List<ServiceRepositoryModel>>(fileName);
                }
                catch (FileNotFoundException)
                {
                    mLogger.LogError("The file {filename} was not found", fileName);
                    readWithException = true;
                }
                catch (Exception ex)
                {
                    mLogger.LogError("{exception}", ex);
                    readWithException = true;
                }
                finally
                {
                    if (!readWithException)
                    {
                        foreach (var repository in repositories)
                        {
                            mLogger.LogInformation("RepositoriesName: {RepositoriesName} RepositoriesOwner: {RepositoriesOwner} read and added to download list", repository.RepositoriesName, repository.RepositoriesOwner);
                            mServiceRepositories.Add(repository);    
                        }

                        repositoryInfoFilesName.Add(fileName);
                    }
                }
            }

            if (repositoryInfoFilesName.Count > 0) 
            {
                await mJsonRepository.SerializeAndSaveJsonFilesAsync(repositoryInfoFilesName, mDownloadRepositoriesFilesNamePath);
                mLogger.LogInformation("Create repeository file name {FilesNamePath}", mDownloadRepositoriesFilesNamePath);
            }


            mLogger.LogInformation("=== Step 2. Check Repositories Info Files Finished ===");
        }

        private async Task DownloadRepositoriesInfoAsync()
        {
            mLogger.LogInformation("=== Step 3. Download Service Info Files ===");

            List<string> serviceInfoFilesName = [];
            int i = 0;

            foreach (ServiceRepositoryModel serviceRepository in mServiceRepositories)
            {
                i++;

                bool savedWithException = false;
                string filePath = "service_info.json";

                if (!string.IsNullOrEmpty(serviceRepository.ServicesListFilePath))
                    filePath = serviceRepository.ServicesListFilePath;

                mLogger.LogInformation("{counter}. Start download service info file from RepositoriesName:{RepositoriesName} RepositoriesOwner:{RepositoriesOwner} FileName:{ServicesListFilePath}", i, serviceRepository.RepositoriesName, serviceRepository.RepositoriesOwner, filePath);
                string fileName = $"{serviceRepository.RepositoriesOwner.ToLower()}.{serviceRepository.RepositoriesName.ToLower()}.info.json";
                
                try
                {
                    byte[] fileContent = await mGitHubClient.Repository.Content.GetRawContent(serviceRepository.RepositoriesOwner, serviceRepository.RepositoriesName, filePath);
                    string serviceRepositoriesList = System.Text.Encoding.Default.GetString(fileContent);
                    await mJsonRepository.SaveRawJsonFilesAsync(serviceRepositoriesList, fileName);
                }
                catch (Exception ex) 
                {
                    savedWithException = true;
                    mLogger.LogError("{exception}", ex);
                }
                finally
                {
                    if (!savedWithException)
                    {
                        mLogger.LogInformation("{сounter}. Download service info file and save as {filename} ", i, fileName);
                        serviceInfoFilesName.Add(fileName);
                    }
                }    
            }

            if (serviceInfoFilesName.Count > 0)
            {
                await mJsonRepository.SerializeAndSaveJsonFilesAsync(serviceInfoFilesName, mDownloadInfoFilesNamePath);
                mLogger.LogInformation("Create service info file name {FilesNamePath}", mDownloadInfoFilesNamePath);
            }

            mLogger.LogInformation("=== Step 3. Download Service Info Files Finished ===");
        }

        public async Task DownloadFromSourceAsync()
        {
            mLogger.LogInformation("=== Step 4. Download From Source ===");

            //List<string> serviceInfoFilesName = [];
            int i = 0;

            foreach (ServiceRepositoryModel serviceRepository in mServiceRepositories)
            {
                i++;

                bool savedWithException = false;
                //string filePath = "service_info.json";

                //if (!string.IsNullOrEmpty(serviceRepository.ServicesListFilePath))
                //    filePath = serviceRepository.ServicesListFilePath;

                //mLogger.LogInformation("{counter}. Start download service info file from RepositoriesName:{RepositoriesName} RepositoriesOwner:{RepositoriesOwner} FileName:{ServicesListFilePath}", i, serviceRepository.RepositoriesName, serviceRepository.RepositoriesOwner, filePath);
                //string fileName = $"{serviceRepository.RepositoriesOwner.ToLower()}.{serviceRepository.RepositoriesName.ToLower()}.info.json";

                try
                {
                    byte[] archiveBytes = await mGitHubClient.Repository.Content.GetArchive(serviceRepository.RepositoriesOwner, serviceRepository.RepositoriesName, ArchiveFormat.Zipball);
                    mLogger.LogInformation("{counter}{repositoriesName} downloading", i, serviceRepository.RepositoriesName);
                    await File.WriteAllBytesAsync($"{serviceRepository.RepositoriesName}.zip", archiveBytes);
                    mLogger.LogInformation("{counter}{repositoriesName} saved", i, serviceRepository.RepositoriesName);
                    ZipFile.ExtractToDirectory($"{serviceRepository.RepositoriesName}.zip", $"{serviceRepository.RepositoriesName}");
                    mLogger.LogInformation("{counter}{repositoriesName} extracted", i, serviceRepository.RepositoriesName);

                    //string serviceRepositoriesList = System.Text.Encoding.Default.GetString(fileContent);
                    //await mJsonRepository.SaveRawJsonFilesAsync(serviceRepositoriesList, fileName);
                }
                catch (Exception ex)
                {
                    savedWithException = true;
                    mLogger.LogError("{exception}", ex);
                }
                finally
                {
                    if (!savedWithException)
                    {
                        //mLogger.LogInformation("{сounter}. Download service info file and save as {filename} ", i, fileName);
                        //serviceInfoFilesName.Add(fileName);
                    }
                }
            }

            /*if (serviceInfoFilesName.Count > 0)
            {
                await mJsonRepository.SerializeAndSaveJsonFilesAsync(serviceInfoFilesName, mDownloadInfoFilesNamePath);
                mLogger.LogInformation("Create service info file name {FilesNamePath}", mDownloadInfoFilesNamePath);
            }*/

            mLogger.LogInformation("=== Step 3. Download Service Info Files Finished ===");
        }

        public void Dispose()
        {
            mLogger.LogInformation("=== UpdateService. Dispose ===");
            mJsonRepository.CreateOrClearRepositoryDirectory();
            mServiceRepositories.Clear();
            mSettingsServiceRepositories.Clear();
        }

        #endregion

        #region OnRaise events

        protected virtual  void OnRaiseDownloadAndCheckUpdateStartedEvent()
        {
            DownloadAndCheckUpdateStartedEventHandler raiseEvents = RaiseDownloadAndCheckUpdateStartedEvent;
            raiseEvents?.Invoke(this);
        }

        protected virtual void OnRaiseDownloadAndCheckUpdateFinishedEvent()
        {
            DownloadAndCheckUpdateFinishedEventHandler raiseEvents = RaiseDownloadAndCheckUpdateFinishedEvent;
            raiseEvents?.Invoke(this);
        }

        #endregion
    }
}
