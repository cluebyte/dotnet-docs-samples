﻿/*
 * Copyright (c) 2019 Google LLC.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy of
 * the License at
 *
 * http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations under
 * the License.
 */

using Google.Cloud.Diagnostics.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using Npgsql;
using System;
using System.Data.Common;
using System.Data;
using Polly;

namespace CloudSql
{
    public class Startup
    {
        public IConfiguration Configuration { get; }
        IServiceCollection _services;

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        // This method gets called by the runtime. Use this method to add
        // services to the container.
        // For more information on how to configure your application, visit
        // http://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            string projectId = Google.Api.Gax.Platform.Instance().ProjectId;
            if (!string.IsNullOrEmpty(projectId))
            {
                services.AddGoogleExceptionLogging(options =>
                {
                    options.ProjectId = projectId;
                    options.ServiceName = "CloudSqlSample";
                    options.Version = "Test";
                });
            }
            services.AddSingleton(typeof(DbConnection), (IServiceProvider) =>
                InitializeDatabase());
            services.AddMvc(options =>
            {
                options.Filters.Add(typeof(DbExceptionFilterAttribute));
            });
            _services = services;
        }

        DbConnection InitializeDatabase()
        {
            DbConnection connection;
            string database = Configuration["CloudSQL:Database"];
            connection = GetPostgreSqlConnection();
            connection.Open();
            using (var createTableCommand = connection.CreateCommand())
            {
                createTableCommand.CommandText = @"
                    CREATE TABLE IF NOT EXISTS
                    votes(
                        vote_id SERIAL NOT NULL,
                        time_cast timestamp NOT NULL,
                        candidate VARCHAR(6) NOT NULL,
                        PRIMARY KEY (vote_id)   
                    )";
                createTableCommand.ExecuteNonQuery();
            }
            return connection;
        }

        DbConnection NewPostgreSqlConnection()
        {
            // [START cloud_sql_postgres_dotnet_ado_connection]
            var connectionString = new NpgsqlConnectionStringBuilder(
                Configuration["CloudSql:ConnectionString"])
            // ConnectionString is set in appsettings.json formatted as follows
            // depending upon where your app is running:
            // Running Locally ConnectionString: 
            // "Uid=aspnetuser;Pwd=;Host=127.0.0.1;Database=votes"
            // Cloud Run ConnectionString: 
            // "Server=/cloudsql/your-project-id:us-central1:instance-name;Uid=aspnetuser;Pwd=;Database=votes"
            // App Engine ConnectionString: 
            // "Uid=aspnetuser;Pwd=;Host=cloudsql;Database=votes"
            {
                // Connecting to a local proxy that does not support ssl.
                SslMode = SslMode.Disable
            };
            connectionString.Pooling = true;
            // [START_EXCLUDE]
            // [START cloud_sql_postgres_dotnet_ado_limit]
            // MaxPoolSize sets maximum number of connections allowed in the pool. 
            connectionString.MaxPoolSize = 5;
            // MinPoolSize sets the minimum number of connections in the pool.
            connectionString.MinPoolSize = 0;
            // [END cloud_sql_postgres_dotnet_ado_limit]
            // [START cloud_sql_postgres_dotnet_ado_timeout]
            // Timeout sets the time to wait (in seconds) while
            // trying to establish a connection before terminating the attempt.
            connectionString.Timeout = 15;
            // [END cloud_sql_postgres_dotnet_ado_timeout]
            // [START cloud_sql_postgres_dotnet_ado_lifetime]
            // ConnectionIdleLifetime sets the time (in seconds) to wait before
            // closing idle connections in the pool if the count of all
            // connections exceeds MinPoolSize.
            connectionString.ConnectionIdleLifetime = 300;
            // [END cloud_sql_postgres_dotnet_ado_lifetime]
            // [END_EXCLUDE]
            NpgsqlConnection connection =
                new NpgsqlConnection(connectionString.ConnectionString);
            // [END cloud_sql_postgres_dotnet_ado_connection]
            return connection;
        }

        DbConnection GetPostgreSqlConnection()
        {
            // [START cloud_sql_postgres_dotnet_ado_backoff]
            var connection = Policy
                .HandleResult<DbConnection>(conn => conn.State != ConnectionState.Open)
                .WaitAndRetry(new[]
                {
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(5)
                }, (result, timeSpan, retryCount, context) =>
                {
                    // Log any warnings here.
                })
                .Execute(() => NewPostgreSqlConnection());
            // [END cloud_sql_postgres_dotnet_ado_backoff]
            return connection;
        }


        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env,
            ILoggerFactory loggerFactory)
        {
            loggerFactory.AddConsole(Configuration.GetSection("Logging"));

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                // Configure error reporting service.
                app.UseGoogleExceptionLogging();
                app.UseExceptionHandler("/Home/Error");
            }

            app.UseMvc(routes =>
            {
                routes.MapRoute(
                    name: "default",
                    template: "{controller=Home}/{action=Index}/{id?}");
            });
        }
    }
}
