#define NONEST

var table = "MemberComm"; // Fill in table name
var schema = "dbo";
var tableMetaData = new TableMetaDataBuilder(this.Connection);
var tableProperties = tableMetaData.GetProperties(table, schema);

// Build Entity
EntityBuilder.BuildEntity(table, schema, tableProperties).Dump("Entity");

// Build DTO
var namespacePath = "NameSpace.Path.Dto"; // Fill in dto namespace path
var namespacePathSubDir = ".SubPathCanBeAnEmptyString"; // Fill in dto namespace sub directory path
var includeAuditProps = false; // Include audit props: ModifiedBy, ModifiedOn || ModifiedDate, CreatedBy, CreatedOn || ModifiedDate

DtoBuilder.BuildDto(tableProperties, table, namespacePath += namespacePathSubDir, includeAuditProps).Dump("Dto");

public static class EntityBuilder
{
	public static string BuildEntity(string tableName, string schema, ICollection<TableProperty> properties)
	{
		var sb = new StringBuilder($"\t[Table(\"{tableName}\", schmea = \"{schema}\")]\r\n");
		sb.Append($"\tpublic class {Shared.NameSanitize(tableName)}\r\n\t{{\r\n");

		properties = properties
			.OrderBy(x => !x.IsKey)
			.ThenBy(x => x.ColumnName)
			.ToList();

		var recordingProperties = new List<TableProperty>();

		bool firstProp = true;
		foreach (var prop in properties)
		{
			if (prop.ColumnName.StartsWith("Create") || prop.ColumnName.StartsWith("Modif"))
			{
				recordingProperties.Add(prop);
				continue;
			}

			WriteProperty(sb, prop, properties, firstProp);
			firstProp = false;
		}

		if (recordingProperties.Any())
		{
			sb.Append("\r\n\t\t// Start Recording Properties\r\n");
			foreach (var property in recordingProperties)
			{
				WriteProperty(sb, property, properties, firstProp);
			}
			sb.Append("\t\t// End Recording Properties\r\n");
		}

		var navProperties = properties
			.Where(x => !string.IsNullOrWhiteSpace(x.PrimaryTable))
			.ToList();

		if (navProperties.Any())
		{
			sb.Append("\r\n\t\t/* Start Nav Properties\r\n");

			foreach (var prop in navProperties)
			{
				sb.Append($"\t\t[ForeignKey(\"{Shared.NameSanitize(prop.ColumnName)}\")]\r\n");
				sb.Append($"\t\tpublic virtual {Shared.NameSanitize(prop.PrimaryTable)} {Shared.NameSanitize(prop.PrimaryTable)} {{ get; set; }}\r\n");
			}

			sb.Append("\t\tEnd Nav Properties */\r\n");
		}

		var dependentTables = properties
			.SelectMany(x => x.DependentTables)
			.ToList();

		if (dependentTables.Any())
		{
			sb.Append("\r\n\t\t/* Start Collection Nav Properties\r\n");

			foreach (var prop in dependentTables)
			{
				sb.Append($"\t\tpublic virtual ICollection<{Shared.NameSanitize(prop)}> {Shared.NameSanitize(prop)} {{ get; set; }} = new HashSet<{Shared.NameSanitize(prop)}>();\r\n");
			}

			sb.Append("\t\tEnd Collection Nav Properties */\r\n");
		}

		sb.Append("\t}");

		return sb.ToString();
	}

	private static void WriteProperty(StringBuilder sb, TableProperty prop, ICollection<TableProperty> properties, bool firstProp)
	{
		var attributes = GetPropertyAttributes(prop, properties);
		if (attributes.Any() && !firstProp)
		{
			sb.Append("\r\n");
		}

		sb.Append("\t\t");
		foreach (var attr in attributes)
		{
			sb.Append($"[{attr.Name}");

			var defaultParam = attr.Params.SingleOrDefault(x => string.IsNullOrWhiteSpace(x.Key));
			var attrParams = attr.Params
				.Where(x => !string.IsNullOrWhiteSpace(x.Key))
				.Aggregate(defaultParam.Value ?? string.Empty, (acc, cur) => $"{acc}, {cur.Key} = {cur.Value}")
				.Trim(new[] { ',', ' ' });

			if (attrParams.Length > 0)
			{
				sb.Append($"({attrParams})");
			}

			sb.Append("]\r\n\t\t");
		}

		sb.Append($"public {Shared.DataMap[prop.DataType]}");

		if (prop.IsNullable && Shared.NonNullableMap.Contains(prop.DataType))
		{
			sb.Append("?");
		}

		sb.Append($" {Shared.NameSanitize(prop.ColumnName)} {{ get; set; }}");
		sb.Append("\r\n");
	}


	private static ICollection<AttrMetaData> GetPropertyAttributes(TableProperty prop, ICollection<TableProperty> properties)
	{
		return AttrMetaDataBuilder.GetPropertyAttributes(prop, properties);
	}

	private class AttrMetaDataBuilder
	{
		private static readonly HashSet<string> UnicodeTypes = new HashSet<string>
		{
			"nvarchar", "nchar"
		};

		private static readonly HashSet<string> MinLengthTypes = new HashSet<string>
		{
			"char", "nchar"
		};

		public static ICollection<AttrMetaData> GetPropertyAttributes(TableProperty prop, ICollection<TableProperty> properties)
		{
			var attributes = new List<AttrMetaData>();

			void MaybeAddAttr(Func<TableProperty, ICollection<TableProperty>, ICollection<AttrMetaData>> func)
			{
				var attrs = func(prop, properties);

				if (attrs != null && attrs.Any())
				{
					foreach (var attr in attrs)
					{
						var existing = attributes.SingleOrDefault(x => x.Name == attr.Name);
						if (existing != null)
						{
							existing.Params = existing.Params
								.Concat(attr.Params)
								.GroupBy(x => x.Key)
								.Select(x => x.First())
								.ToList();

							continue;
						}

						attributes.Add(attr);
					}
				}
			}

			MaybeAddAttr(Unicode);
			MaybeAddAttr(Key);
			MaybeAddAttr(StringLength);
			MaybeAddAttr(Computed);
			MaybeAddAttr(Identity);
			MaybeAddAttr(DbNone);
			MaybeAddAttr(Required);
			MaybeAddAttr(Rename);
			MaybeAddAttr(DataType);

			return attributes;
		}

		private static ICollection<AttrMetaData> Rename(TableProperty prop, ICollection<TableProperty> properties)
		{
			var sanitizedName = Shared.NameSanitize(prop.ColumnName);

			if (prop.ColumnName.Length == sanitizedName.Length)
			{
				return null;
			}

			var attr = new AttrMetaData { Name = "Column" };
			attr.Params.Add(new KeyValuePair<string, string>("", $"\"{prop.ColumnName}\""));

			return new[] { attr };
		}

		private static readonly HashSet<string> StringTypes = new HashSet<string>
		{
			"char"
			, "varchar"
			, "nchar"
			, "nvarchar"
			, "varbinary"
		};


		private static readonly HashSet<string> NumericTypes = new HashSet<string>
		{
			"numeric"
			, "decimal"
		};

		private static ICollection<AttrMetaData> DataType(TableProperty prop, ICollection<TableProperty> properties)
		{
			if (StringTypes.Contains(prop.DataType))
			{
				var attr = new AttrMetaData { Name = "Column" };
				attr.Params.Add(new KeyValuePair<string, string>("TypeName", $"\"{prop.DataType}({(prop.Size > -1 ? prop.Size.ToString() : "MAX")})\""));
				return new[] { attr };
			}


			if (NumericTypes.Contains(prop.DataType))
			{
				var attr = new AttrMetaData { Name = "Column" };
				attr.Params.Add(new KeyValuePair<string, string>("TypeName", $"\"{prop.DataType}({prop.Precision}, {prop.Scale})\""));
				return new[] { attr };
			}

			return null;
		}

		private static ICollection<AttrMetaData> Required(TableProperty prop, ICollection<TableProperty> properties)
		{
			if (prop.IsNullable || !Shared.NullableMap.Contains(prop.DataType))
			{
				return null;
			}

			return new[] { new AttrMetaData { Name = "Required" } };
		}

		private static ICollection<AttrMetaData> StringLength(TableProperty prop, ICollection<TableProperty> properties)
		{
			if (prop.Size == null || prop.Size == -1)
			{
				return null;
			}

			var attr = new AttrMetaData { Name = "StringLength" };
			attr.Params.Add(new KeyValuePair<string, string>("", prop.Size.ToString()));

			if (MinLengthTypes.Contains(prop.DataType))
			{
				attr.Params.Add(new KeyValuePair<string, string>("MinimumLength", prop.Size.ToString()));
			}

			return new[] { attr }; ;
		}

		private static ICollection<AttrMetaData> Computed(TableProperty prop, ICollection<TableProperty> properties)
		{
			if (!prop.IsComputed)
			{
				return null;
			}

			var attr = new AttrMetaData { Name = "DatabaseGenerated" };
			attr.Params.Add(new KeyValuePair<string, string>("", "DatabaseGeneratedOption.Computed"));

			return new[] { attr }; ;
		}

		private static ICollection<AttrMetaData> Identity(TableProperty prop, ICollection<TableProperty> properties)
		{
			if (!prop.IsIdentity || prop.IsKey)
			{
				return null;
			}

			var attr = new AttrMetaData { Name = "DatabaseGenerated" };
			attr.Params.Add(new KeyValuePair<string, string>("", "DatabaseGeneratedOption.Identity"));

			return new[] { attr }; ;
		}

		private static ICollection<AttrMetaData> DbNone(TableProperty prop, ICollection<TableProperty> properties)
		{
			if (!prop.IsKey || prop.IsIdentity)
			{
				return null;
			}

			// composite PK not db generated is implied
			if (properties.Count(x => x.IsKey) > 1)
			{
				return null;
			}

			var attr = new AttrMetaData { Name = "DatabaseGenerated" };
			attr.Params.Add(new KeyValuePair<string, string>("", "DatabaseGeneratedOption.None"));

			return new[] { attr }; ;
		}

		private static ICollection<AttrMetaData> Key(TableProperty prop, ICollection<TableProperty> properties)
		{
			if (!prop.IsKey)
			{
				return null;
			}

			var attrs = new List<AttrMetaData> { new AttrMetaData { Name = "Key" } };

			if (properties.Count(x => x.IsKey) > 1)
			{
				var order = -1;
				for (int i = 0; i < properties.Count; i++)
				{
					var tableProperty = properties.ElementAt(i);
					if (tableProperty.IsKey)
					{
						order++;

						if (tableProperty.ColumnName == prop.ColumnName)
						{
							var columnAttr = new AttrMetaData { Name = "Column" };
							columnAttr.Params.Add(new KeyValuePair<string, string>("Order", order.ToString()));
							attrs.Add(columnAttr);
						}
					}
				}
			}

			return attrs;
		}

		private static ICollection<AttrMetaData> Unicode(TableProperty prop, ICollection<TableProperty> properties)
		{
			if (!UnicodeTypes.Contains(prop.DataType))
			{
				return null;
			}

			return new[] { new AttrMetaData { Name = "IsUnicode" } };
		}
	}

	private class AttrMetaData
	{
		public string Name { get; set; }
		public ICollection<KeyValuePair<string, string>> Params { get; set; } = new HashSet<KeyValuePair<string, string>>();
	}
}

public class TableMetaDataBuilder
{
	private readonly IDbConnection _connection;

	public TableMetaDataBuilder(IDbConnection connection)
	{
		_connection = connection;

		if (_connection.State != ConnectionState.Open)
		{
			_connection.Open();
		}
	}

	public List<TableProperty> GetProperties(string table, string schema)
	{
		const string sql = @" -- get columns query
				SELECT
					c.COLUMN_NAME AS ColumnName
					, c.ORDINAL_POSITION AS Position
					, c.IS_NULLABLE AS IsNullable
					, c.DATA_TYPE AS DataType
					, c.CHARACTER_MAXIMUM_LENGTH AS Size
					, c.NUMERIC_PRECISION AS Precision
					, c.NUMERIC_SCALE AS Scale
				FROM    INFORMATION_SCHEMA.COLUMNS c
				WHERE   c.TABLE_NAME = @TableName and ISNULL(@TableSchema, c.TABLE_SCHEMA) = c.TABLE_SCHEMA  
				ORDER BY c.ORDINAL_POSITION";

		var pks = GetPk(table, schema);
		var computeds = GetComputed(table, schema);
		var identities = GetIdentity(table, schema);
		var fks = GetFks(table, schema);
		var dependentTables = GetDependentTables(table, schema);

		var properties = new List<TableProperty>();
		using (var cmd = new SqlCommand(sql, (SqlConnection)_connection))
		{
			cmd.Parameters.Add(new SqlParameter("@TableName", table));
			cmd.Parameters.Add(new SqlParameter("@TableSchema", schema));

			using (SqlDataReader reader = cmd.ExecuteReader())
			{
				while (reader.Read())
				{
					var size = default(int?);
					if (int.TryParse(reader["Size"].ToString(), out var tmpSize))
					{
						size = tmpSize;
					}

					var precision = default(int?);
					if (int.TryParse(reader["Precision"].ToString(), out var tmpPrecision))
					{
						precision = tmpPrecision;
					}

					var scale = default(int?);
					if (int.TryParse(reader["Scale"].ToString(), out var tmpScale))
					{
						scale = tmpScale;
					}

					var colName = reader["ColumnName"].ToString();

					var prop = new TableProperty
					{
						ColumnName = colName,
						Position = int.Parse(reader["Position"].ToString()),
						DataType = reader["DataType"].ToString(),
						PrimaryTable = fks
							.Where(x => x.column == colName)
							.Select(x => x.table)
							.FirstOrDefault(),
						DependentTables = dependentTables
							.Where(x => x.refColumn == colName)
							.Select(x => x.dependentTable)
							.ToList(),
						IsNullable = reader["IsNullable"].ToString() == "YES",
						Size = size,
						Precision = precision,
						Scale = scale,
						IsComputed = computeds.Contains(colName),
						IsIdentity = identities.Contains(colName),
						IsKey = pks.Contains(colName),
					};

					properties.Add(prop);
				}
			}
		}

		return properties;
	}

	private List<string> GetPk(string table, string schema)
	{
		const string sql = @" -- PK columns
				SELECT ku.COLUMN_NAME AS Name
				FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS AS tc
				INNER JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE AS ku
				    ON tc.CONSTRAINT_TYPE = 'PRIMARY KEY' 
				    AND tc.CONSTRAINT_NAME = ku.CONSTRAINT_NAME
					AND tc.CONSTRAINT_SCHEMA = ku.CONSTRAINT_SCHEMA
					AND tc.TABLE_CATALOG = ku.TABLE_CATALOG
					AND tc.TABLE_NAME = ku.TABLE_NAME
				WHERE ku.TABLE_NAME = @TableName and ISNULL(@TableSchema, ku.TABLE_SCHEMA) = ku.TABLE_SCHEMA";

		var properties = new List<string>();
		using (var cmd = new SqlCommand(sql, (SqlConnection)_connection))
		{
			cmd.Parameters.Add(new SqlParameter("@TableName", table));
			cmd.Parameters.Add(new SqlParameter("@TableSchema", schema));

			using (SqlDataReader reader = cmd.ExecuteReader())
			{
				while (reader.Read())
				{
					properties.Add(reader["Name"].ToString());
				}
			}
		}

		return properties;
	}

	private List<string> GetIdentity(string table, string schema)
	{
		const string sql = @" -- identity columns
				select COLUMN_NAME AS Name
				from INFORMATION_SCHEMA.COLUMNS
				where COLUMNPROPERTY(object_id('AspNetUsers'), COLUMN_NAME, 'IsIdentity') = 1
				AND TABLE_NAME = @TableName and ISNULL(@TableSchema, TABLE_SCHEMA) = TABLE_SCHEMA";

		var properties = new List<string>();
		using (var cmd = new SqlCommand(sql, (SqlConnection)_connection))
		{
			cmd.Parameters.Add(new SqlParameter("@TableName", table));
			cmd.Parameters.Add(new SqlParameter("@TableSchema", schema));

			using (SqlDataReader reader = cmd.ExecuteReader())
			{
				while (reader.Read())
				{
					properties.Add(reader["Name"].ToString());
				}
			}
		}

		return properties;
	}

	private List<string> GetComputed(string table, string schema)
	{
		const string sql = @" -- computed columns
				SELECT Name
				FROM sys.computed_columns
				WHERE object_id = OBJECT_ID(@TableSchema + '.' + @TableName)";

		var properties = new List<string>();
		using (var cmd = new SqlCommand(sql, (SqlConnection)_connection))
		{
			cmd.Parameters.Add(new SqlParameter("@TableName", table));
			cmd.Parameters.Add(new SqlParameter("@TableSchema", schema));

			using (SqlDataReader reader = cmd.ExecuteReader())
			{
				while (reader.Read())
				{
					properties.Add(reader["Name"].ToString());
				}
			}
		}

		return properties;
	}

	private List<(string dependentTable, string refColumn)> GetDependentTables(string table, string schema)
	{
		const string sql = @" -- Dependent tables
				SELECT
				   OBJECT_NAME(f.parent_object_id) AS DependentTable,
				   col.name AS ReferenceColumn
				FROM 
				   sys.foreign_keys AS f
				INNER JOIN 
				   sys.foreign_key_columns AS fc 
				      ON f.OBJECT_ID = fc.constraint_object_id
				INNER JOIN 
				   sys.tables t 
				      ON t.OBJECT_ID = f.referenced_object_id
				INNER JOIN
					sys.columns col
					  ON col.object_id = t.object_id AND column_id = fc.referenced_column_id
				WHERE 
				   f.referenced_object_id = OBJECT_ID(@TableSchema + '.' + @TableName)";

		var properties = new List<(string, string)>();
		using (var cmd = new SqlCommand(sql, (SqlConnection)_connection))
		{
			cmd.Parameters.Add(new SqlParameter("@TableName", table));
			cmd.Parameters.Add(new SqlParameter("@TableSchema", schema));

			using (SqlDataReader reader = cmd.ExecuteReader())
			{
				while (reader.Read())
				{
					properties.Add((reader["DependentTable"].ToString(), reader["ReferenceColumn"].ToString()));
				}
			}
		}

		return properties;
	}

	private List<(string column, string table)> GetFks(string table, string schema)
	{
		const string sql = @" -- fk columns
				select 
				    col.name as ColumnName,
				    pk_tab.name as PrimaryTable
				from sys.tables tab
				    inner join sys.columns col 
				        on col.object_id = tab.object_id
				    left outer join sys.foreign_key_columns fk_cols
				        on fk_cols.parent_object_id = tab.object_id
				        and fk_cols.parent_column_id = col.column_id
				    left outer join sys.foreign_keys fk
				        on fk.object_id = fk_cols.constraint_object_id
				    left outer join sys.tables pk_tab
				        on pk_tab.object_id = fk_cols.referenced_object_id
				    left outer join sys.columns pk_col
				        on pk_col.column_id = fk_cols.referenced_column_id
				        and pk_col.object_id = fk_cols.referenced_object_id
				where fk.object_id is not null AND tab.name = @TableName
					AND ISNULL(@TableSchema, schema_name(tab.schema_id)) = schema_name(tab.schema_id)
				order by schema_name(tab.schema_id) + '.' + tab.name,
				    col.column_id";

		var properties = new List<(string, string)>();
		using (var cmd = new SqlCommand(sql, (SqlConnection)_connection))
		{
			cmd.Parameters.Add(new SqlParameter("@TableName", table));
			cmd.Parameters.Add(new SqlParameter("@TableSchema", schema));

			using (SqlDataReader reader = cmd.ExecuteReader())
			{
				while (reader.Read())
				{
					properties.Add((reader["ColumnName"].ToString(), reader["PrimaryTable"].ToString()));
				}
			}
		}

		return properties;
	}
}

public static class DtoBuilder
{
	public static string BuildDto(List<TableProperty> entityDto, string tableName, string namespacePath, bool includeAuditProps = false)
	{
		var dtoBuilder = new StringBuilder();
		var toEntityBuilder = new StringBuilder();

		dtoBuilder.AppendLine("using System;");

		dtoBuilder.AppendLine("using CareBook.Business.Models;" + "\n");
		dtoBuilder.AppendLine("namespace " + namespacePath + "\n{");
		dtoBuilder.AppendLine("\tpublic class " + tableName + "Dto" + "\n\t{");

		foreach (var property in entityDto)
		{
			var columnName = Shared.NameSanitize(property.ColumnName);

			if ((columnName == "CreatedOn"
				||columnName == "CreatedDate"
				|| columnName == "ModifiedOn"
				|| columnName == "ModifiedDate"
				|| columnName == "CreatedBy"
				|| columnName == "ModifiedBy")
				&& !includeAuditProps)
			{
				continue;
			}
			else if (columnName == "CreatedOn" || columnName == "CreatedDate" ||  columnName == "ModifiedOn" || columnName == "ModifiedDate")
			{
				dtoBuilder.AppendLine("\t\t[JsonConverter(typeof(UtcDateJsonConverter))]");
			}

			dtoBuilder.AppendLine(string.Format("\t\tpublic {0}{1} {2} {3}",
					Shared.DataMap[property.DataType],
					property.IsNullable && Shared.NonNullableMap.Contains(property.DataType) ? "?" : string.Empty,
					Shared.NameSanitize(property.ColumnName),
					"{ get; set; }"));

			toEntityBuilder.AppendLine("\t\t\t\tentity." + Shared.NameSanitize(property.ColumnName) + " = " + Shared.NameSanitize(property.ColumnName) + ";");
		}

		dtoBuilder.AppendLine("\n\t\tpublic " + tableName + " ToEntity(" + tableName + " entity)\n\t\t{");
		dtoBuilder.AppendLine("\t\t\t\tif (entity == null)\n\t\t\t\t{\n\t\t\t\t\t\treturn null;\n\t\t\t\t}\n");
		dtoBuilder.Append(toEntityBuilder.ToString());
		dtoBuilder.AppendLine("\n\t\t\t\treturn entity;\n\t\t}\n\t}\n}");

		return dtoBuilder.ToString();
	}
}

public static class Shared
{
	public static readonly Dictionary<string, string> DataMap = new Dictionary<string, string>
	{
		{ "bigint", "long" }
		,{ "bit", "bool" }
		,{ "char", "string" }
		,{ "nchar", "string" }
		,{ "date", "DateTime" }
		,{ "datetime", "DateTime" }
		,{ "datetime2", "DateTime" }
		,{ "decimal", "decimal" }
		,{ "float", "decimal" }
		,{ "hierarchyid", "NotSupported" }
		,{ "int", "int" }
		,{ "money", "decimal" }
		,{ "numeric", "decimal" }
		,{ "nvarchar", "string" }
		,{ "smallint", "small" }
		,{ "time", "TimeSpan" }
		,{ "uniqueidentifier", "Guid" }
		,{ "varbinary", "byte[]" }
		,{ "varchar", "string" }
		,{ "xml", "string" }
	};

	public static readonly HashSet<string> NonNullableMap = new HashSet<string>
	{
		"bigint"
		, "bit"
		, "date"
		, "datetime"
		, "datetime2"
		, "decimal"
		, "float"
		, "int"
		, "money"
		, "numeric"
		, "smallint"
		, "time"
		, "uniqueidentifier"
	};

	public static readonly HashSet<string> NullableMap = new HashSet<string>
	{
		"char"
		, "nchar"
		, "nvarchar"
		, "varbinary"
		, "varchar"
		, "xml"
	};


	public static string NameSanitize(string name)
	{
		name = name.Replace("_", " ");
		if (name.Contains(" "))
		{
			name = name.Split(new[] { ' ' })
			.Aggregate(string.Empty, (acc, curr) =>
			{
				if (IsAllUpper(curr))
				{
					curr = curr.ToLower();
				}

				if (char.IsLower(curr[0]))
				{
					curr = char.ToUpper(curr[0]) + curr.Remove(0, 1);
				}

				return $"{acc}{curr}";
			});
		}

		if (IsAllUpper(name))
		{
			name = name.ToLower();
			name = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name);
		}

		var upperRun = new List<char>();
		for (int i = 0; i < name.Length; i++)
		{
			var character = name[i];
			if (char.IsUpper(character))
			{
				upperRun.Add(character);
				continue;
			}

			if (upperRun.Count < 3)
			{
				upperRun.Clear();
				continue;
			}

			var newString = new string(upperRun.ToArray()).ToLower();
			newString = char.ToUpper(newString[0]) + newString.Remove(0, 1);
			newString = newString.Remove(newString.Length - 1, 1) + char.ToUpper(newString[newString.Length - 1]);
			name = name.Replace(new string(upperRun.ToArray()), newString);
			upperRun.Clear();
		}

		if (upperRun.Count > 1)
		{
			var newString = new string(upperRun.ToArray()).ToLower();
			newString = char.ToUpper(newString[0]) + newString.Remove(0, 1);
			name = name.Replace(new string(upperRun.ToArray()), newString);
		}

		return name;
	}

	private static bool IsAllUpper(string input)
	{
		for (int i = 0; i < input.Length; i++)
		{
			if (Char.IsLetter(input[i]) && !Char.IsUpper(input[i]))
				return false;
		}
		return true;
	}

}

public class TableProperty
{
	public string ColumnName { get; set; }
	public int Position { get; set; }
	public bool IsNullable { get; set; }
	public string DataType { get; set; }
	public string PrimaryTable { get; set; }
	public List<string> DependentTables { get; set; }
	public int? Size { get; set; }
	public int? Precision { get; set; }
	public int? Scale { get; set; }
	public bool IsKey { get; set; }
	public bool IsComputed { get; set; }
	public bool IsIdentity { get; set; }
}
