﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Elasticsearch.Net;

namespace Serilog.Sinks.Elasticsearch.Durable
{
    /// <summary>
    /// 
    /// </summary>
    public class ElasticsearchPayloadReader: APayloadReader<List<string>>
    {
        private readonly string _pipelineName;
        private readonly string _typeName;
        private readonly Func<object, string> _serialize;
        private readonly Func<string, DateTime,string> _getIndexForEvent;
        private readonly ElasticOpType _elasticOpType;
        private readonly RollingInterval _rollingInterval;
        private List<string> _payload;
        private int _count;
        private DateTime _date;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="pipelineName"></param>
        /// <param name="typeName"></param>
        /// <param name="serialize"></param>
        /// <param name="getIndexForEvent"></param>
        /// <param name="elasticOpType"></param>
        /// <param name="rollingInterval"></param>
        public ElasticsearchPayloadReader(string pipelineName, string typeName, Func<object, string> serialize,
            Func<string, DateTime, string> getIndexForEvent, ElasticOpType elasticOpType, RollingInterval rollingInterval)
        {
            if ((int)rollingInterval < (int)RollingInterval.Day)
            {
                throw new ArgumentException("Rolling intervals less frequent than RollingInterval.Day are not supported");
            }
            
            _pipelineName = pipelineName;
            _typeName = typeName;
            _serialize = serialize;
            _getIndexForEvent = getIndexForEvent;
            _elasticOpType = elasticOpType;
            _rollingInterval = rollingInterval;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public override List<string> GetNoPayload()
        {
            return new List<string>();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="filename"></param>
        protected override void InitPayLoad(string filename)
        {
            _payload = new List<string>();
            _count = 0;
            var fileNameParts = filename.Split('-');
            // VT-5543: Temporary workaround to support old date format for hourly files, like 'buffer-serilog-20220418-16.json' or 'buffer-serilog-20220418-16_001.json'
            if (TryParseOldFormattedDate(fileNameParts))
                return;

            var lastToken = fileNameParts.Last();

            // lastToken should be something like 20150218.json or 20150218_3.json now
            if (!lastToken.ToLowerInvariant().EndsWith(".json"))
            {
                throw new FormatException(string.Format("The file name '{0}' does not seem to follow the right file pattern - it must be named [whatever]-{{Date}}[_n].json", Path.GetFileName(filename)));
            }

            var dateFormat = _rollingInterval.GetFormat();
            var dateString = lastToken.Substring(0, dateFormat.Length);
            _date = DateTime.ParseExact(dateString, dateFormat, CultureInfo.InvariantCulture);
        }

        // VT-5543: Could be removed after migration
        /// <summary>
        /// 
        /// </summary>
        /// <param name="fileNameParts"></param>
        /// <returns></returns>
        private bool TryParseOldFormattedDate(string[] fileNameParts)
        {
            if (_rollingInterval == RollingInterval.Hour && fileNameParts.Length > 2)
            {
                var oldDateHourlyFormat = RollingIntervalExtensions.OldHourlyDateFormat;
                var oldFormatDateExpression =
                    new Regex("^(?<date>" + RollingIntervalExtensions.OldHourlyDateRegExp + ")(?<sequence>_[0-9]{3,}){0,1}\\.json$");
                var last2Parts = fileNameParts.Skip(fileNameParts.Length - 2);
                var last2PartsString = string.Join("-", last2Parts);
                var match = oldFormatDateExpression.Match(last2PartsString);
                if (match.Success)
                {
                    var oldDateString = match.Groups["date"].Value;
                    _date = DateTime.ParseExact(oldDateString, oldDateHourlyFormat, CultureInfo.InvariantCulture);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
       /// 
       /// </summary>
       /// <returns></returns>
        protected override List<string> FinishPayLoad()
        {
            return _payload;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="nextLine"></param>
        protected override void AddToPayLoad(string nextLine)
        {
            // No need to add empty or whitespace lines to payload
            if (string.IsNullOrWhiteSpace(nextLine))
            {
                return;
            }

            var indexName = _getIndexForEvent(nextLine, _date);
            var action = ElasticsearchSink.CreateElasticAction(
                opType: _elasticOpType,
                indexName: indexName, pipelineName: _pipelineName,
                id: _count + "_" + Guid.NewGuid(),
                mappingType: _typeName);
            var actionJson = LowLevelRequestResponseSerializer.Instance.SerializeToString(action);

            _payload.Add(actionJson);
            _payload.Add(nextLine);

            _count++;
        }
    }
}
