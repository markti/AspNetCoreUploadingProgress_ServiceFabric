// Copyright © 2017 Dmitry Sikorsky. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileUploadActor.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Microsoft.ServiceFabric.Actors;
using Microsoft.ServiceFabric.Actors.Client;
using Microsoft.ServiceFabric.Data;

namespace StatefulWebUpload
{
    [Route("uploader")]
    public class UploaderController : Controller
    {
        const string PERSON_ACTOR = "fabric:/ServiceFabricUploader/FileUploadActorService";

        private IHostingEnvironment hostingEnvironment;

        public UploaderController(IHostingEnvironment hostingEnvironment)
        {
            this.hostingEnvironment = hostingEnvironment;
        }

        [HttpGet]
        public string Get()
        {
            return Guid.NewGuid().ToString();
        }

        private async Task<int> GetProgressFromActor(string id)
        {
            int progress = 0;
            try
            {
                var cancelToken = new CancellationToken();

                // construct the actor id from the user id
                var actorId = new ActorId(id);

                IFileUploadActor actor = ActorProxy.Create<IFileUploadActor>(actorId, new Uri(PERSON_ACTOR));

                progress = await actor.GetProgressAsync(cancelToken);
            }
            catch (Exception ex)
            {
                //.ActorMessage(this, "Actor activated.");
                ServiceEventSource.Current.Write(ex.ToString());
            }
            return progress;
        }
        private async Task SetProgressOnActor(string id, int progress)
        {
            try
            {
                var cancelToken = new CancellationToken();

                // construct the actor id from the user id
                var actorId = new ActorId(id);

                IFileUploadActor actor = ActorProxy.Create<IFileUploadActor>(actorId, new Uri(PERSON_ACTOR));

                await actor.SetProgressAsync(progress, cancelToken);
            }
            catch (Exception ex)
            {
                //.ActorMessage(this, "Actor activated.");
                ServiceEventSource.Current.Write(ex.ToString());
            }
            return;
        }

        [Route("{id}")]
        [HttpPost]
        public async Task<IActionResult> Post(string id, IList<IFormFile> files)
        {

            long totalBytes = files.Sum(f => f.Length);

            foreach (IFormFile source in files)
            {
                string filename = ContentDispositionHeaderValue.Parse(source.ContentDisposition).FileName.ToString().Trim('"');

                filename = this.EnsureCorrectFilename(filename);

                byte[] buffer = new byte[16 * 1024];

                using (FileStream output = System.IO.File.Create(this.GetPathAndFilename(filename)))
                {
                    using (Stream input = source.OpenReadStream())
                    {
                        long totalReadBytes = 0;
                        int readBytes;

                        while ((readBytes = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            await output.WriteAsync(buffer, 0, readBytes);
                            totalReadBytes += readBytes;
                            int progress = (int)((float)totalReadBytes / (float)totalBytes * 100.0);
                            await SetProgressOnActor(id, progress);
                            await Task.Delay(10); // It is only to make the process slower
                        }
                    }
                }
            }

            return this.Content("success");
        }

        [Route("{id}/progress")]
        [HttpPost]
        public async Task<int> Progress(string id)
        {
            return await GetProgressFromActor(id);
        }

        private string EnsureCorrectFilename(string filename)
        {
            if (filename.Contains("\\"))
            filename = filename.Substring(filename.LastIndexOf("\\") + 1);

            return filename;
        }

        private string GetPathAndFilename(string filename)
        {
            string path = this.hostingEnvironment.WebRootPath + "\\uploads\\";

            if (!Directory.Exists(path))
            Directory.CreateDirectory(path);

            return path + filename;
        }
    }
}