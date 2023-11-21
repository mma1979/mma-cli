# MMA CLI

![build and test](https://img.shields.io/github/actions/workflow/status/abpframework/abp/build-and-test.yml?branch=dev&style=flat-square)
[![NuGet](https://img.shields.io/badge/nugett-v3.1.3-blue?style=flat-square)](https://www.nuget.org/packages/mma-cli)
[![NuGet (with prereleases)](https://img.shields.io/badge/nugett-v4.0.0%20rc.6-yellow?style=flat-square)](https://www.nuget.org/packages/mma-cli/4.0.0-rc.6)

```
.___  ___. .___  ___.      ___      
|   \/   | |   \/   |     /   \     
|  \  /  | |  \  /  |    /  ^  \    
|  |\/|  | |  |\/|  |   /  /_\  \   
|  |  |  | |  |  |  |  /  _____  \  
|__|  |__| |__|  |__| /__/     \__\ 
                                    
```

MMA Cli is a complete **Code Generator** based on **ASP.NET Core** to create **modern APIs** by following the software development **best practices** and the **latest technologies**.



## Getting Started

- [Quick Start](#) is a single-part, quick-start tutorial to create a solution with the `mma cli`. 
- [Interactive Mode](#) can be used to create and run ABP based solutions with different options and details.
- [Command Line Mode](#) is a complete tutorial to develop a full stack web application with all aspects of a real-life solution.
- [UI](#) is a complete tutorial to develop a full stack web application with all aspects of a real-life solution.

### Quick Start
Install the `mma-cli`:

````bash
dotnet tool install --global mma-cli --version 4.0.0-rc.6
````

Create a new Solution:

````bash
mma new SolutionName
````

you can also use short form

````bash
mma n SolutionName
````

#### available options.

`--mapper` tom indecat which object mapping libirary that accept `mapster` or `atomapper` and default value is `automapper`

````bash
mma n SolutionName --mapper mapster
````

> Note: The UI version supports only AutoMapper

Now, we need to navigate to the solution folder to start add components.

````bash
cd SolutionName
````

1. Add Entity:
	````bash
	mma generate entity MyEntity long
	# OR
	mma g e MyEntity long
	````

	generate entity also require `--mapper` value and the default value is `automapper`

	````bash 
	mma g e MyEntity long --mapper Mapster
	````
	generate property requires `EntityName`,`PK Data type`

	supported data types:
	- Guid
	- int
	- long
	- string
	- decimal
	- floot
	- bool
	- DateTime
	
	Default data type is `Guid`

	#### available options.

	- `--mapper` tom indecat which object mapping libirary that accept `mapster` or `atomapper` and default value is `automapper`

	- `--no-api` to skip generaate API controller for this entity.
	
2. Add/Remove Property to exsiting Entity
	````bash
	mma generate property MyEntity MyProperty long
	# OR
	mma g p MyEntity MyProperty long true
	````

	generate property requires `EntityName`, `PropertyName`, `DataType`

	supported data types:
	- Guid
	- int
	- long
	- string
	- decimal
	- floot
	- bool
	- DateTime
	
	Default data type is `Guid`

	#### available options.

	- `--remove` to remove the property from existing entity
	   ````bash 
		mma g p MyEntity MyProperty long true --remove
		````

2. Add/Remove relation to exsiting Entities
	````bash
	mma generate relation MyParentEntity MyChildEntity ForeignKeyProperty
	# OR
	mma g r MyParentEntity MyChildEntity ForeignKeyProperty
	````

	generate property requires `Referece Entit Name`, `Child Entity Name`, `ForeignKey PropertyName`


	#### available options.

	- `--remove` to remove the property from existing entity
	   ````bash 
		mma g r MyParentEntity MyChildEntity ForeignKeyProperty --remove
		````


### Interactive Mode

You can use interactive mode by enter command `mma` without any arguments in solution path

![image](https://i.imgur.com/DpTWnj2.gif)

### UI

	Also you can use UI mode to create and manage your solutions. Select `UI` from `Interactive` or execute `mma ui`

	```bash
	mma ui
	```

	![image](https://i.imgur.com/0HnJstp.png)

	![image](https://i.imgur.com/mkQKfvc.png)

	![image](https://i.imgur.com/C28low8.png)

	![image](https://i.imgur.com/VBrwxCR.png)

	![image](https://i.imgur.com/7mJBqy1.png)
